using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lantrn.Infra;

// A pending invite. Only the token's hash is stored, so the link is shown once, when the invite is created.
public sealed class Invitation
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public required string TokenHash { get; set; }

    public string? InvitedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.Email);
    }
}
