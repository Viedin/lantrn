using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

public sealed class Collection
{
    // Also names the Qdrant collection, so names can repeat between owners and never reach Qdrant.
    public Guid Id { get; set; }

    // Unique per owner.
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string OwnerId { get; set; }

    public bool IsPrivate { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Document> Documents { get; set; } = [];
}

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        // AccountService deletes a removed user's collections first; this only catches one created meanwhile.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.OwnerId, c.Name }).IsUnique();
    }
}
