using System.Security.Cryptography;
using System.Text;
using Lantrn.Infra;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services;

public sealed record UserSummary(string Id, string Email, bool IsAdmin);

public sealed record InvitationSummary(Guid Id, string Email, string? InvitedBy, DateTime CreatedAt, DateTime ExpiresAt);

// Users, roles and invites. Each call gets its own scope: a Blazor circuit would otherwise keep one
// UserManager, and the DbContext behind it, for as long as the page is open.
public sealed class AccountService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<DatabaseContext> dbFactory,
    ILogger<AccountService> logger)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    // Registration decides between "first user, becomes admin" and "needs an invite"; two at once must not both be first.
    private readonly SemaphoreSlim registration = new(1, 1);

    // Run once at startup: the role must exist, and an install from before roles keeps its users' access.
    public async Task EnsureAdminAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            await roles.CreateAsync(new IdentityRole(Roles.Admin));
        }

        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        // Every account used to have full access, so nobody is locked out of the workspace they had.
        foreach (var user in await users.Users.ToListAsync())
        {
            await users.AddToRoleAsync(user, Roles.Admin);
            logger.LogInformation("Made {Email} an admin: no admin existed yet", user.Email);
        }
    }

    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var adminIds = await AdminIds(db).ToListAsync(cancellationToken);
        var users = await db.Users.OrderBy(u => u.Email).Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);
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

    public async Task<IdentityResult> DeleteUserAsync(string userId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByIdAsync(userId) is not { } user)
        {
            return IdentityResult.Success;
        }
        if (await users.IsInRoleAsync(user, Roles.Admin) && (await users.GetUsersInRoleAsync(Roles.Admin)).Count <= 1)
        {
            return Failed("The last admin can't be removed.");
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
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new InvalidOperationException($"{email} already has an account.");
        }

        await db.Invitations.Where(i => i.Email.ToUpper() == normalized).ExecuteDeleteAsync(cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTime.UtcNow;
        db.Invitations.Add(new Invitation
        {
            Email = email,
            TokenHash = Hash(token),
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
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        return await db.Invitations
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

            var first = !await db.Users.AnyAsync();
            Invitation? invitation = null;
            if (!first)
            {
                var hash = Hash(token ?? "");
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
        from userRole in db.UserRoles
        join role in db.Roles on userRole.RoleId equals role.Id
        where role.Name == Roles.Admin
        select userRole.UserId;

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static IdentityResult Failed(string description) => IdentityResult.Failed(new IdentityError { Description = description });
}

public sealed record RegistrationResult(IdentityResult Result, ApplicationUser? User);
