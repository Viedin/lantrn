using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

public sealed class Collection
{
    // Also the Qdrant collection name. Qdrant cannot rename a collection, so the name doubles as the key.
    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Document> Documents { get; set; } = [];
}

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.HasKey(c => c.Name);
    }
}
