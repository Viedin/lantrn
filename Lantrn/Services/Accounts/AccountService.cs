using System.Security.Claims;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    IOptions<OidcOptions> oidc,
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
        return await SetAdminAsync(users, user, admin);
    }

    private async Task<IdentityResult> SetAdminAsync(UserManager<ApplicationUser> users, ApplicationUser user, bool admin)
    {
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

    // A known login signs straight in. A new one is linked to the account with its verified email; with no such account
    // it needs an invite or auto-provisioning, unless it's the very first account, which becomes admin like a registration.
    public async Task<ExternalSignInResult> SignInExternalAsync(ExternalLoginInfo info)
    {
        await registration.WaitAsync();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // A subject is only unique at its issuer, so logins are kept per issuer: after switching providers, a
            // reused subject can't sign into an old account, and people are matched by email again instead.
            var provider = info.Principal.FindFirst("sub")?.Issuer ?? info.LoginProvider;
            var user = await users.FindByLoginAsync(provider, info.ProviderKey);
            if (user is null)
            {
                var email = info.Principal.FindFirstValue("email");
                if (string.IsNullOrEmpty(email) || !(oidc.Value.TrustEmail || HasVerifiedEmail(info.Principal)))
                {
                    logger.LogWarning("Refused a login from {Provider} without a verified email ({Email})", provider, email ?? "none");
                    return new ExternalSignInResult(null, ExternalSignInError.NoVerifiedEmail);
                }

                user = await users.FindByEmailAsync(email);
                if (user is null)
                {
                    var created = await CreateExternalUserAsync(scope.ServiceProvider.GetRequiredService<DatabaseContext>(), users, email);
                    if (created.User is null)
                    {
                        return created;
                    }
                    user = created.User;
                }

                var linked = await users.AddLoginAsync(user, new UserLoginInfo(provider, info.ProviderKey, info.ProviderDisplayName));
                if (!linked.Succeeded)
                {
                    logger.LogWarning("Couldn't link a login from {Provider} to {Email}: {Errors}", provider, email, Describe(linked));
                    return new ExternalSignInResult(null, ExternalSignInError.Failed);
                }
                logger.LogInformation("Linked a login from {Provider} to {Email}", provider, email);
            }

            if (oidc.Value.AdminGroup is { Length: > 0 } adminGroup)
            {
                // Some providers leave the claim out for someone in no groups, so this can be expected; a provider
                // that sends none at all, though, demotes every admin but the last.
                if (!info.Principal.HasClaim(c => c.Type == OidcOptions.GroupsClaim))
                {
                    logger.LogWarning("{Email} signed in without a groups claim, so isn't in {AdminGroup}. If they should be, " +
                        "check that the provider sends groups and that Oidc__Scope asks for them", user.Email, adminGroup);
                }
                var synced = await SetAdminAsync(users, user, info.Principal.HasClaim(OidcOptions.GroupsClaim, adminGroup));
                if (!synced.Succeeded)
                {
                    logger.LogWarning("Kept the admin role for {Email}: {Errors}", user.Email, Describe(synced));
                }
            }

            return new ExternalSignInResult(user, null);
        }
        finally
        {
            registration.Release();
        }
    }

    private async Task<ExternalSignInResult> CreateExternalUserAsync(DatabaseContext db, UserManager<ApplicationUser> users, string email)
    {
        var first = !await db.Users.AsNoTracking().AnyAsync();
        var normalized = email.ToUpperInvariant();
        var now = DateTime.UtcNow;
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Email.ToUpper() == normalized && i.ExpiresAt > now);
        if (!first && invitation is null && !oidc.Value.AutoProvision)
        {
            logger.LogInformation("Refused single sign-on for {Email}: no account or invite", email);
            return new ExternalSignInResult(null, ExternalSignInError.NotInvited, email);
        }

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
        {
            logger.LogWarning("Couldn't create an account for {Email}: {Errors}", email, Describe(result));
            return new ExternalSignInResult(null, ExternalSignInError.Failed);
        }

        if (first)
        {
            await users.AddToRoleAsync(user, Roles.Admin);
        }
        if (invitation is not null)
        {
            db.Invitations.Remove(invitation);
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Created account {Email} through single sign-on{Admin}", email, first ? " as the first admin" : "");
        return new ExternalSignInResult(user, null);
    }

    // Anyone can put any address on an account at some providers; only a verified one says who they are.
    private static bool HasVerifiedEmail(ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    private static IQueryable<string> AdminIds(DatabaseContext db) =>
        db.UserRoles
            .AsNoTracking()
            .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin))
            .Select(ur => ur.UserId);

    private static IdentityResult Failed(string description) => IdentityResult.Failed(new IdentityError { Description = description });
}

public sealed record RegistrationResult(IdentityResult Result, ApplicationUser? User);

public enum ExternalSignInError { Failed, NoVerifiedEmail, NotInvited }

// Email is the address the provider gave, when that is what the error is about.
public sealed record ExternalSignInResult(ApplicationUser? User, ExternalSignInError? Error, string? Email = null);
