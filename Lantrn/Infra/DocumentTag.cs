using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

// Mirrors the "tags" payload on the document's Qdrant points, so tags can be listed and counted without Qdrant.
public sealed class DocumentTag
{
    public Guid DocumentId { get; set; }

    public required string Name { get; set; }
}

public sealed class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.HasKey(t => new { t.DocumentId, t.Name });
        builder.HasIndex(t => t.Name);
    }
}
