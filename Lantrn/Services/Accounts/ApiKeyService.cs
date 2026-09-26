using System.Security.Cryptography;
using System.Text;
using Lantrn.Infra;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services.Accounts;

public sealed record ApiKeySummary(Guid Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt);

// Keys for the public API. Each user manages their own; a key is returned once and only its hash is kept.
public sealed class ApiKeyService(IDbContextFactory<DatabaseContext> dbFactory, ILogger<ApiKeyService> logger)
{
    // Marks the string as a Lantrn key, so a leaked one is easy to recognise and search for.
    public const string KeyPrefix = "lnt_";

    // Last used is only a hint, so a busy key isn't written on every request.
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeySummary(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt))
            .ToListAsync(cancellationToken);
    }

    // Returns the key itself; it can't be read back later.
    public async Task<string> CreateAsync(string userId, string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100)
        {
            throw new ArgumentException("Give the key a name of at most 100 characters.");
        }

        var key = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ApiKeys.Add(new ApiKey
        {
            UserId = userId,
            Name = name,
            Prefix = key[..(KeyPrefix.Length + 6)],
            KeyHash = Hash(key),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created API key '{Name}' for user {UserId}", name, userId);
        return key;
    }

    // Scoped to the user, so nobody revokes someone else's key by guessing its id.
    public async Task RevokeAsync(string userId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.ApiKeys.Where(k => k.Id == id && k.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Revoked API key {KeyId} of user {UserId}", id, userId);
    }

    // The id of the user the key belongs to, or null when it isn't a key we issued.
    public async Task<string?> FindUserIdAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hash = Hash(key);
        var match = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == hash)
            .Select(k => new { k.Id, k.UserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var stale = now - LastUsedPrecision;
        await db.ApiKeys
            .Where(k => k.Id == match.Id && (k.LastUsedAt == null || k.LastUsedAt < stale))
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), cancellationToken);

        return match.UserId;
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
