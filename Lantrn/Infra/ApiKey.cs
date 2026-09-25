namespace Lantrn.Infra;

// A key for the public API. It acts as its user, with whatever roles they have when it is used.
// Only the key's hash is stored, so the key itself is shown once, when it is created.
public sealed class ApiKey
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public required string Name { get; set; }

    // The key's first characters, so it can be recognised in a list without being stored.
    public required string Prefix { get; set; }

    public required string KeyHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}
