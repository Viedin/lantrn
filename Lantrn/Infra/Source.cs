using Lantrn.Services.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

// A website kept in sync with a collection. Synced documents point back at their source,
// so a sync can remove the ones that disappeared there.
public sealed class Source
{
    public Guid Id { get; set; }

    public Guid CollectionId { get; set; }

    // The start URL.
    public required string Location { get; set; }

    // Only pages under this path are crawled, e.g. "/docs".
    public string? Scope { get; set; }

    public int MaxPages { get; set; }

    public List<string> Tags { get; set; } = [];

    public IngestOptions Chunking { get; set; } = new();

    // Null syncs only when asked to.
    public TimeSpan? SyncInterval { get; set; }

    // Whoever last saved the website; its pages are credited to them.
    public required string AddedById { get; set; }

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
        // AccountService removes a removed user's websites; this only covers one saved meanwhile.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.AddedById)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ComplexProperty(s => s.Chunking);
    }
}
