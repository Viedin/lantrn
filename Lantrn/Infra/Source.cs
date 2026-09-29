using Lantrn.Services.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

// Somewhere documents are kept in sync from. Synced documents point back at their source,
// so a sync can remove the ones that disappeared there.
public sealed class Source
{
    public static readonly Guid DocumentsFolderId = new("5f0c3a8e-0d4b-4d0e-9a51-6f2f6c1d7e01");

    public Guid Id { get; set; }

    public SourceKind Kind { get; set; }

    // Where a website's pages go. The documents folder picks a collection per top-level folder and uses this for the rest.
    public Guid CollectionId { get; set; }

    // The start URL of a website, or the path of a folder.
    public required string Location { get; set; }

    // Only pages under this path are crawled, e.g. "/docs".
    public string? Scope { get; set; }

    public int MaxPages { get; set; }

    public List<string> Tags { get; set; } = [];

    public IngestOptions Chunking { get; set; } = new();

    // Null syncs only when asked to; the documents folder syncs continuously regardless.
    public TimeSpan? SyncInterval { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(s => s.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ComplexProperty(s => s.Chunking);
    }
}

public enum SourceKind
{
    Folder,
    Website,
}
