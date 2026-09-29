using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

public sealed class Collection
{
    // The documents folder syncs its loose files into this one, so it always exists.
    public static readonly Guid DefaultId = new("0b6e2f4a-3c1d-4e8f-9a2b-7d5c6e1f0a01");

    public const string DefaultName = "documents";

    // Also names the Qdrant collection, so names can repeat between owners and never reach Qdrant.
    public Guid Id { get; set; }

    // Unique per owner.
    public required string Name { get; set; }

    public string? Description { get; set; }

    // Null for collections the admins manage, such as the default one and those synced from the documents folder.
    public string? OwnerId { get; set; }

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

        // Admin-managed names (a null owner) are kept unique by DocumentStore, since the database treats nulls as distinct.
        builder.HasIndex(c => new { c.OwnerId, c.Name }).IsUnique();
    }
}
