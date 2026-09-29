namespace Lantrn.Services.Ingestion;

// What to ingest and where. Saved next to the job's inbox file, so a restart picks the job up again.
// The file name decides how the bytes are read; the source is what the document is stored under.
// A re-embed works from the stored document instead, and has no bytes.
public sealed record IngestRequest(
    Guid CollectionId,
    string Source,
    string FileName,
    IReadOnlyList<string> Tags,
    IngestOptions Options,
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

public sealed class IngestJob(Guid id, IngestRequest request, string filePath, bool ownsFile)
{
    public Guid Id { get; } = id;

    public IngestRequest Request { get; } = request;

    // An inbox file the job deletes once done, or a file in the documents folder that stays.
    public string FilePath { get; } = filePath;

    public bool OwnsFile { get; } = ownsFile;

    public IngestJobStatus Status { get; set; }

    public string? ContentHash { get; set; }

    public Guid? DocumentId { get; set; }

    // Without the embeddings, which only take up memory once stored.
    public IngestResult? Result { get; set; }

    public string? Error { get; set; }

    public bool IsFinished => Status is IngestJobStatus.Done or IngestJobStatus.Failed or IngestJobStatus.Cancelled;
}

// Counts for one run of the queue: from a job added while it was idle until it is idle again.
public sealed class IngestTally
{
    public int Total { get; set; }
    public int Done { get; set; }
    public int Stored { get; set; }
    public int Chunks { get; set; }
    public int Failed { get; set; }
    public string? Step { get; set; }
    public bool Stopping { get; set; }
}
