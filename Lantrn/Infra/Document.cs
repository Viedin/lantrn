using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

public sealed class Document
{
    public Guid Id { get; set; }

    public Guid CollectionId { get; set; }

    // File name for uploads, URL for crawled pages; the same value Qdrant stores as "source".
    public required string Source { get; set; }

    public required string Markdown { get; set; }

    public DocumentKind Kind { get; set; }

    // File name under the originals folder, for documents whose uploaded file is kept (OCR images and PDFs).
    public string? OriginalFile { get; set; }

    // SHA-256 of the ingested bytes, so a sync can skip what hasn't changed.
    public string? ContentHash { get; set; }

    // The website this document is synced from; null for uploads.
    public Guid? SourceId { get; set; }

    // Whoever last uploaded it, or added the website it came from.
    public required string AddedById { get; set; }

    public int ChunkCount { get; set; }

    public DateTime IngestedAt { get; set; }

    public List<DocumentTag> Tags { get; set; } = [];
}

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasOne<Collection>()
            .WithMany(c => c.Documents)
            .HasForeignKey(d => d.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Re-ingesting the same file or URL into a collection refreshes its row instead of adding another.
        builder.HasIndex(d => new { d.CollectionId, d.Source })
            .IsUnique();

        builder.HasMany(d => d.Tags)
            .WithOne()
            .HasForeignKey(t => t.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Removing a source stops the syncing but keeps what it already brought in.
        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(d => d.SourceId)
            .OnDelete(DeleteBehavior.SetNull);

        // AccountService deletes a removed user's documents with their vectors; this only covers one stored meanwhile.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.AddedById)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
