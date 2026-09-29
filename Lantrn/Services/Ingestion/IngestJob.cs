namespace Lantrn.Services.Ingestion;

// What to ingest and where. Saved next to the job's inbox file, so a restart picks the job up again.
// The file name decides how the bytes are read; the source is what the document is stored under.
// A re-embed works from the stored document instead, and has no bytes.
// AddedById is whoever queued it; a re-embedded document stays credited to whoever added it.
public sealed record IngestRequest(
    Guid CollectionId,
    string Source,
    string FileName,
    IReadOnlyList<string> Tags,
    IngestOptions Options,
    string AddedById,
    Guid? SourceId = null,
    bool KeepOriginal = true,
    Guid? ReembedDocumentId = null);

public enum IngestJobStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
}

public sealed class IngestJob(Guid id, IngestRequest request, string filePath)
{
    public Guid Id { get; } = id;

    public IngestRequest Request { get; } = request;

    // The inbox file, deleted once the job is done.
    public string FilePath { get; } = filePath;

    public IngestJobStatus Status { get; set; }

    public Guid? DocumentId { get; set; }

    // Without the embeddings, which only take up memory once stored.
    public IngestResult? Result { get; set; }

    public string? Error { get; set; }

    // What the job is doing right now, such as which chunks are being embedded.
    public string? Step { get; set; }

    public bool IsFinished => Status is IngestJobStatus.Done or IngestJobStatus.Failed or IngestJobStatus.Cancelled;
}

// What someone's jobs add up to.
public sealed record IngestTally(int Total, int Done, int Stored, int Chunks, int Failed, string Step)
{
    public static IngestTally Of(IReadOnlyList<IngestJob> jobs)
    {
        var done = jobs.Count(j => j.IsFinished);
        var stored = jobs.Count(j => j.Status == IngestJobStatus.Done);
        var running = jobs.FirstOrDefault(j => j.Status == IngestJobStatus.Running);
        var step = running is not null ? running.Step ?? $"Processing {running.Request.Source}"
            : done < jobs.Count ? "Waiting for other uploads to finish"
            : $"Stored {stored} of {jobs.Count} documents.";

        return new IngestTally(
            jobs.Count,
            done,
            stored,
            jobs.Sum(j => j.Result?.Chunks.Count ?? 0),
            jobs.Count(j => j.Status == IngestJobStatus.Failed),
            step);
    }
}
