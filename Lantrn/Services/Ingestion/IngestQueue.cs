using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Lantrn.Services.Ingestion;

/// <summary>
/// Everything waiting to be ingested, worked through one at a time by IngestWorker. Uploads are written to
/// the inbox folder first, so closing the page or restarting the app doesn't lose them.
/// </summary>
public sealed class IngestQueue(IOptions<StorageOptions> storage, IHostEnvironment environment, ILogger<IngestQueue> logger)
{
    public const long MaxFileSize = 100 * 1024 * 1024;
    private const int MaxArchiveEntries = 1000;
    private const long MaxArchiveBytes = 500 * 1024 * 1024;
    private const int MaxRecentJobs = 200;

    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly Channel<IngestJob> channel = Channel.CreateUnbounded<IngestJob>();
    private readonly Lock gate = new();
    private readonly List<IngestJob> recent = [];
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);

    private readonly Dictionary<(Guid CollectionId, string Source, string ContentHash), DateTime> failed = [];
    private CancellationTokenSource? running;
    private CancellationTokenSource stop = new();

    public string InboxPath { get; } = Path.Combine(
        Path.GetFullPath(storage.Value.DataPath, environment.ContentRootPath), "inbox");

    public IngestTally Tally { get; private set; } = new();

    // Raised from background threads; components re-render through InvokeAsync.
    public event Action? Changed;

    // Tripped by Stop, so a website sync stops fetching pages along with the queue.
    public CancellationToken StopToken
    {
        get
        {
            lock (gate)
            {
                return stop.Token;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (gate)
            {
                return recent.Any(j => !j.IsFinished);
            }
        }
    }

    // Newest first.
    public IReadOnlyList<IngestJob> Recent
    {
        get
        {
            lock (gate)
            {
                return [.. recent];
            }
        }
    }

    // A ZIP archive becomes one job per supported file inside it, tagged with the folders it sat in. Returns how many jobs were added.
    public async Task<int> EnqueueUploadAsync(IngestRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InboxPath);
        var id = Guid.NewGuid();
        var path = DataPath(id);

        try
        {
            await using (var file = File.Create(path))
            {
                await CopyLimitedAsync(content, file, MaxFileSize, cancellationToken);
            }

            if (!DocumentExtractor.IsArchive(request.FileName))
            {
                await AddAsync(id, request, cancellationToken);
                return 1;
            }

            var added = await UnpackAsync(request, path, cancellationToken);
            File.Delete(path);
            return added;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public async Task EnqueueAsync(IngestRequest request, byte[] bytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InboxPath);
        var id = Guid.NewGuid();
        await File.WriteAllBytesAsync(DataPath(id), bytes, cancellationToken);
        await AddAsync(id, request, cancellationToken);
    }

    // Kept in the inbox like an upload, just without bytes, so a restart still finishes the re-embed.
    public Task EnqueueReembedAsync(Guid collectionId, Guid documentId, string source, CancellationToken cancellationToken = default) =>
        EnqueueAsync(
            new IngestRequest(collectionId, source, source, [], new IngestOptions(), ReembedDocumentId: documentId), [], cancellationToken);

    // For a file that stays where it is, in the documents folder. Not kept across restarts: the next sync finds it again.
    public void EnqueueFile(IngestRequest request, string path) => Add(new IngestJob(Guid.NewGuid(), request, path, ownsFile: false));

    public bool IsPending(Guid collectionId, string source)
    {
        lock (gate)
        {
            return recent.Any(j => !j.IsFinished && j.Request.CollectionId == collectionId && j.Request.Source == source);
        }
    }

    // So a sync doesn't keep queuing a file that failed as it is, such as a broken PDF. Kept apart from the recent
    // jobs, which are cleared. Retried after a while, in case the failure was the embedding server being down.
    public bool HasFailed(Guid collectionId, string source, string contentHash)
    {
        lock (gate)
        {
            return failed.TryGetValue((collectionId, source, contentHash), out var failedAt)
                   && DateTime.UtcNow - failedAt < RetryFailedAfter;
        }
    }

    public void ReportStep(string step)
    {
        Tally.Step = step;
        NotifyChanged();
    }

    public void StopAll()
    {
        lock (gate)
        {
            foreach (var job in recent.Where(j => j.Status == IngestJobStatus.Queued))
            {
                job.Status = IngestJobStatus.Cancelled;
                Tally.Done++;
            }

            Tally.Stopping = true;
            running?.Cancel();
            stop.Cancel();
            stop = new CancellationTokenSource();
        }

        NotifyChanged();
    }

    // A finished run is only kept while someone is looking at it. Until then the tally only grows, so a crawl that
    // fetches slower than the worker ingests still counts as one run.
    public void ClearIfIdle()
    {
        lock (gate)
        {
            if (recent.Count == 0 || recent.Any(j => !j.IsFinished))
            {
                return;
            }

            recent.Clear();
            Tally = new IngestTally();
        }

        NotifyChanged();
    }

    internal ChannelReader<IngestJob> Reader => channel.Reader;

    // Queues the uploads that a restart interrupted, and removes files left by an upload that never finished.
    internal async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(InboxPath))
        {
            return;
        }

        // Only what was there before this run: an upload may already be writing its files.
        var leftovers = Directory.EnumerateFiles(InboxPath)
            .Select(p => new FileInfo(p))
            .Where(f => f.CreationTimeUtc < startedAt)
            .OrderBy(f => f.CreationTimeUtc);

        foreach (var path in leftovers)
        {
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path.Name), out var id))
            {
                continue;
            }

            if (path.Extension != ".json")
            {
                if (!File.Exists(RequestPath(id)))
                {
                    path.Delete();
                }
                continue;
            }

            // Half written when the app was killed; failing here would stop it from starting at all.
            IngestRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<IngestRequest>(await File.ReadAllTextAsync(path.FullName, cancellationToken));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Dropping the unreadable queued ingest {JobId}", id);
                request = null;
            }

            if (request is null || !File.Exists(DataPath(id)))
            {
                DeleteInboxFiles(id);
                continue;
            }

            Add(new IngestJob(id, request, DataPath(id), ownsFile: true));
            logger.LogInformation("Resumed the queued ingest of {Source} into collection {CollectionId}", request.Source, request.CollectionId);
        }
    }

    // Returns null for a job that was stopped while it waited.
    internal CancellationTokenSource? Start(IngestJob job, CancellationToken stoppingToken)
    {
        lock (gate)
        {
            if (job.Status == IngestJobStatus.Cancelled)
            {
                if (job.OwnsFile)
                {
                    DeleteInboxFiles(job.Id);
                }
                return null;
            }

            job.Status = IngestJobStatus.Running;
            running = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            return running;
        }
    }

    internal void Complete(IngestJob job, IngestJobStatus status)
    {
        lock (gate)
        {
            job.Status = status;
            running = null;
            Tally.Done++;
            if (status == IngestJobStatus.Done)
            {
                Tally.Stored++;
                Tally.Chunks += job.Result?.Chunks.Count ?? 0;
                foreach (var key in failed.Keys.Where(f => f.CollectionId == job.Request.CollectionId && f.Source == job.Request.Source).ToList())
                {
                    failed.Remove(key);
                }
            }
            else if (status == IngestJobStatus.Failed)
            {
                Tally.Failed++;
                if (job.ContentHash is { } hash)
                {
                    failed[(job.Request.CollectionId, job.Request.Source, hash)] = DateTime.UtcNow;
                }
            }

            if (!recent.Any(j => !j.IsFinished))
            {
                Tally.Step = $"{(Tally.Stopping ? "Stopped. " : string.Empty)}Stored {Tally.Stored} of {Tally.Total} documents.";
                Tally.Stopping = false;
            }
        }

        if (job.OwnsFile)
        {
            DeleteInboxFiles(job.Id);
        }

        NotifyChanged();
    }

    private async Task AddAsync(Guid id, IngestRequest request, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(RequestPath(id), JsonSerializer.Serialize(request), cancellationToken);
        Add(new IngestJob(id, request, DataPath(id), ownsFile: true));
    }

    private void Add(IngestJob job)
    {
        lock (gate)
        {
            Tally.Total++;
            recent.Insert(0, job);

            for (var i = recent.Count - 1; i >= 0 && recent.Count > MaxRecentJobs; i--)
            {
                if (recent[i].IsFinished)
                {
                    recent.RemoveAt(i);
                }
            }
        }

        channel.Writer.TryWrite(job);
        NotifyChanged();
    }

    private async Task<int> UnpackAsync(IngestRequest archive, string path, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries
            .Where(e => e.Length > 0 && DocumentExtractor.TryGetContentType(e.Name, out _) && !IsHidden(e.FullName))
            .ToList();

        var tooLarge = $"Archives can hold at most {MaxArchiveEntries} files and {MaxArchiveBytes / 1024 / 1024} MB unpacked.";
        if (entries.Count > MaxArchiveEntries || entries.Sum(e => e.Length) > MaxArchiveBytes)
        {
            throw new InvalidOperationException(tooLarge);
        }

        long unpacked = 0;
        foreach (var entry in entries)
        {
            var id = Guid.NewGuid();
            try
            {
                await using var source = entry.Open();
                await using var target = File.Create(DataPath(id));
                // The declared sizes above can lie, so the archive's budget is also kept on what is actually written.
                unpacked += await CopyLimitedAsync(source, target, MaxFileSize, cancellationToken);
                if (unpacked > MaxArchiveBytes)
                {
                    throw new InvalidOperationException(tooLarge);
                }
            }
            catch
            {
                File.Delete(DataPath(id));
                throw;
            }

            // "handbook.zip" with "hr/leave.pdf" -> source "handbook.zip/hr/leave.pdf", tagged "hr".
            var folders = entry.FullName.Split('/')[..^1];
            await AddAsync(id, archive with
            {
                Source = $"{archive.Source}/{entry.FullName}",
                FileName = entry.Name,
                Tags = DocumentStore.ParseTags(string.Join(',', archive.Tags.Concat(folders))),
            }, cancellationToken);
        }

        logger.LogInformation("Unpacked {Files} files from {Archive}", entries.Count, archive.Source);
        return entries.Count;
    }

    // ".git/config", "__MACOSX/._report.pdf" and the like.
    private static bool IsHidden(string entryPath) =>
        entryPath.Split('/').Any(segment => segment.StartsWith('.') || segment == "__MACOSX");

    // A declared size can lie, so the bytes themselves are counted. Returns how many were copied.
    internal static async Task<long> CopyLimitedAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException($"Files can be at most {maxBytes / 1024 / 1024} MB.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    private string DataPath(Guid id) => Path.Combine(InboxPath, $"{id:N}.bin");

    private string RequestPath(Guid id) => Path.Combine(InboxPath, $"{id:N}.json");

    private void DeleteInboxFiles(Guid id)
    {
        try
        {
            File.Delete(DataPath(id));
            File.Delete(RequestPath(id));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove inbox files of job {JobId}", id);
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
