using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services.Accounts;

public sealed record UserSummary(string Id, string Email, bool IsAdmin);

public sealed record InvitationSummary(Guid Id, string Email, string? InvitedBy, DateTime CreatedAt, DateTime ExpiresAt);

// Users, roles and invites. Each call gets its own scope: a Blazor circuit would otherwise keep one
// UserManager, and the DbContext behind it, for as long as the page is open.
public sealed class AccountService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<DatabaseContext> dbFactory,
    DocumentStore documents,
    SourceStore sources,
    IngestQueue queue,
    SourceSyncService sync,
    ILogger<AccountService> logger)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    // Registration decides between "first user, becomes admin" and "needs an invite"; two at once must not both be first.
    private readonly SemaphoreSlim registration = new(1, 1);

    // Run once at startup, so the first account to register can be made admin.
    public async Task EnsureAdminRoleAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            await roles.CreateAsync(new IdentityRole(Roles.Admin));
        }
    }

    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().AnyAsync(cancellationToken);
    }

    public async Task<string?> GetEmailAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var adminIds = await AdminIds(db).ToListAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);
        return users.Select(u => new UserSummary(u.Id, u.Email ?? "", adminIds.Contains(u.Id))).ToList();
    }

    public async Task<IdentityResult> SetAdminAsync(string userId, bool admin)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByIdAsync(userId) is not { } user)
        {
            return Failed("That user no longer exists.");
        }
        if (await users.IsInRoleAsync(user, Roles.Admin) == admin)
        {
            return IdentityResult.Success;
        }
        if (!admin && (await users.GetUsersInRoleAsync(Roles.Admin)).Count <= 1)
        {
            return Failed("There must be at least one admin.");
        }

        var result = admin ? await users.AddToRoleAsync(user, Roles.Admin) : await users.RemoveFromRoleAsync(user, Roles.Admin);
        if (result.Succeeded)
        {
            // Open pages hold the old roles; a new stamp makes their circuits sign out on the next revalidation.
            await users.UpdateSecurityStampAsync(user);
            logger.LogInformation("{Change} admin role for {Email}", admin ? "Granted" : "Revoked", user.Email);
        }
        return result;
    }

    // A removed admin's content is the workspace's, so it goes to the admin removing them; a user's content is deleted.
    public async Task<IdentityResult> DeleteUserAsync(string userId, string removedById)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByIdAsync(userId) is not { } user)
        {
            return IdentityResult.Success;
        }

        var admin = await users.IsInRoleAsync(user, Roles.Admin);
        if (admin && (await users.GetUsersInRoleAsync(Roles.Admin)).Count <= 1)
        {
            return Failed("The last admin can't be removed.");
        }

        // Their queued jobs and running crawls would only fail once they are gone, after doing all the work.
        queue.Stop(j => j.Request.AddedById == user.Id);
        sync.Stop((site, startedById) => startedById == user.Id || site.AddedById == user.Id);

        if (admin)
        {
            await sources.TransferAsync(user.Id, removedById);
            await documents.TransferContentAsync(user.Id, removedById);
        }
        else
        {
            // Websites first, so a sync can't bring their pages back. Documents go through the store rather than the
            // foreign keys, so their vectors and kept originals go too.
            await sources.RemoveAddedByAsync(user.Id);
            await documents.DeleteContentOfAsync(user.Id);
        }

        var result = await users.DeleteAsync(user);
        if (result.Succeeded)
        {
            logger.LogInformation("Deleted user {Email}", user.Email);
        }
        return result;
    }

    // Returns the token for the invite link; only its hash is kept. A new invite replaces any pending one for the email.
    public async Task<string> CreateInvitationAsync(string email, string? invitedBy, CancellationToken cancellationToken = default)
    {
        email = email.Trim();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var normalized = email.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new InvalidOperationException($"{email} already has an account.");
        }

        await db.Invitations.Where(i => i.Email.ToUpper() == normalized).ExecuteDeleteAsync(cancellationToken);

        var token = Crypto.NewToken();
        var now = DateTime.UtcNow;
        db.Invitations.Add(new Invitation
        {
            Email = email,
            TokenHash = Crypto.Sha256Hex(token),
            InvitedBy = invitedBy,
            CreatedAt = now,
            ExpiresAt = now + InvitationLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{InvitedBy} invited {Email}", invitedBy, email);
        return token;
    }

    public async Task<IReadOnlyList<InvitationSummary>> ListInvitationsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Invitations
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.InvitedBy, i.CreatedAt, i.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeInvitationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Invitations.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    // The email a valid, unexpired invite is for.
    public async Task<string?> FindInvitationAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hash = Crypto.Sha256Hex(token);
        var now = DateTime.UtcNow;
        return await db.Invitations
            .AsNoTracking()
            .Where(i => i.TokenHash == hash && i.ExpiresAt > now)
            .Select(i => i.Email)
            .SingleOrDefaultAsync(cancellationToken);
    }

    // With no users yet the account becomes the first admin; after that it needs an invite, which it uses up.
    public async Task<RegistrationResult> RegisterAsync(string email, string password, string? token)
    {
        await registration.WaitAsync();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

            var first = !await db.Users.AsNoTracking().AnyAsync();
            Invitation? invitation = null;
            if (!first)
            {
                var hash = Crypto.Sha256Hex(token ?? "");
                var now = DateTime.UtcNow;
                invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash && i.ExpiresAt > now);
                if (invitation is null)
                {
                    return new RegistrationResult(Failed("This invite is invalid or has expired. Ask an admin for a new one."), null);
                }
                // The invite is for one address; the form shows it read-only, but the post could say otherwise.
                email = invitation.Email;
            }

            var user = new ApplicationUser { UserName = email, Email = email };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return new RegistrationResult(result, null);
            }

            if (first)
            {
                await users.AddToRoleAsync(user, Roles.Admin);
            }
            else
            {
                db.Invitations.Remove(invitation!);
                await db.SaveChangesAsync();
            }

            logger.LogInformation("Created account {Email}{Admin}", email, first ? " as the first admin" : "");
            return new RegistrationResult(result, user);
        }
        finally
        {
            registration.Release();
        }
    }

    private static IQueryable<string> AdminIds(DatabaseContext db) =>
        db.UserRoles
            .AsNoTracking()
            .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin))
            .Select(ur => ur.UserId);

    private static IdentityResult Failed(string description) => IdentityResult.Failed(new IdentityError { Description = description });
}

public sealed record RegistrationResult(IdentityResult Result, ApplicationUser? User);
