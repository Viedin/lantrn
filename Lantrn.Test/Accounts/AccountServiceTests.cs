using System.Security.Claims;
using Lantrn.Infra;
using Lantrn.Services.Accounts;
using Lantrn.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Lantrn.Test.Accounts;

// Built from the app's own service registrations, so the Identity rules and the wiring are the real ones.
public class AccountServiceTests : IAsyncLifetime
{
    private const string Password = "Correct-Horse-1";

    private readonly string connectionString = $"Data Source=accounts-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection keepAlive;
    private readonly ServiceProvider services;
    private readonly AccountService accounts;

    public AccountServiceTests()
    {
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Lantrn"] = connectionString })
            .Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(Path.Combine(Path.GetTempPath(), "lantrn-tests"));

        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(environment)
            .AddCoreServices(configuration, environment)
            .AddSiteAuthentication(configuration, environment)
            .BuildServiceProvider();
        accounts = services.GetRequiredService<AccountService>();
    }

    public async Task InitializeAsync()
    {
        await using (var db = await services.GetRequiredService<IDbContextFactory<DatabaseContext>>().CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        await accounts.EnsureAdminRoleAsync();
    }

    [Fact]
    public async Task The_first_account_becomes_admin()
    {
        var registered = await accounts.RegisterAsync("first@example.com", Password, null);

        Assert.True(registered.Result.Succeeded);
        Assert.True(await IsAdminAsync("first@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("made-up-token")]
    public async Task Later_accounts_need_an_invite(string? token)
    {
        await accounts.RegisterAsync("first@example.com", Password, null);

        var registered = await accounts.RegisterAsync("second@example.com", Password, token);

        Assert.False(registered.Result.Succeeded);
        Assert.Null(await FindUserAsync("second@example.com"));
    }

    [Fact]
    public async Task An_invite_registers_the_invited_email_whatever_the_form_posts()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        var token = await accounts.CreateInvitationAsync("bob@example.com", "first@example.com");

        var registered = await accounts.RegisterAsync("mallory@example.com", Password, token);

        Assert.True(registered.Result.Succeeded);
        Assert.Equal("bob@example.com", registered.User!.Email);
        Assert.Null(await FindUserAsync("mallory@example.com"));
        Assert.False(await IsAdminAsync("bob@example.com"));
    }

    [Fact]
    public async Task An_invite_is_used_up()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        var token = await accounts.CreateInvitationAsync("bob@example.com", null);
        await accounts.RegisterAsync("bob@example.com", Password, token);

        var again = await accounts.RegisterAsync("bob@example.com", Password, token);

        Assert.False(again.Result.Succeeded);
        Assert.Null(await accounts.FindInvitationAsync(token));
    }

    [Fact]
    public async Task An_expired_invite_is_refused()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        var token = await accounts.CreateInvitationAsync("bob@example.com", null);
        await using (var db = await services.GetRequiredService<IDbContextFactory<DatabaseContext>>().CreateDbContextAsync())
        {
            await db.Invitations.ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        Assert.Null(await accounts.FindInvitationAsync(token));
        Assert.False((await accounts.RegisterAsync("bob@example.com", Password, token)).Result.Succeeded);
    }

    [Fact]
    public async Task A_new_invite_replaces_the_pending_one()
    {
        var first = await accounts.CreateInvitationAsync("bob@example.com", null);
        var second = await accounts.CreateInvitationAsync("Bob@Example.com", null);

        Assert.Null(await accounts.FindInvitationAsync(first));
        Assert.Equal("Bob@Example.com", await accounts.FindInvitationAsync(second));
        Assert.Single(await accounts.ListInvitationsAsync());
    }

    [Fact]
    public async Task Someone_with_an_account_cant_be_invited()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.CreateInvitationAsync("FIRST@example.com", null));
    }

    [Fact]
    public async Task The_last_admin_cant_be_demoted_or_removed()
    {
        var admin = (await accounts.RegisterAsync("first@example.com", Password, null)).User!;

        Assert.False((await accounts.SetAdminAsync(admin.Id, false)).Succeeded);
        Assert.False((await accounts.DeleteUserAsync(admin.Id, admin.Id)).Succeeded);
        Assert.True(await IsAdminAsync("first@example.com"));
    }

    [Fact]
    public async Task An_admin_can_step_down_once_there_is_another()
    {
        var first = (await accounts.RegisterAsync("first@example.com", Password, null)).User!;
        var token = await accounts.CreateInvitationAsync("bob@example.com", null);
        var bob = (await accounts.RegisterAsync("bob@example.com", Password, token)).User!;
        await accounts.SetAdminAsync(bob.Id, true);

        Assert.True((await accounts.SetAdminAsync(first.Id, false)).Succeeded);
        Assert.False(await IsAdminAsync("first@example.com"));
    }

    [Fact]
    public async Task A_new_login_is_linked_to_the_account_with_its_verified_email()
    {
        var first = (await accounts.RegisterAsync("first@example.com", Password, null)).User!;

        var linked = await accounts.SignInExternalAsync(Login("First@Example.com"));
        var again = await accounts.SignInExternalAsync(Login("First@Example.com"));

        Assert.Equal(first.Id, linked.User?.Id);
        Assert.Equal(first.Id, again.User?.Id);
        Assert.Single(await accounts.ListUsersAsync());
    }

    [Fact]
    public async Task An_unverified_email_is_neither_linked_nor_given_an_account()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        await accounts.CreateInvitationAsync("bob@example.com", null);
        Oidc.AutoProvision = true;

        Assert.Equal(ExternalSignInError.NoVerifiedEmail, (await accounts.SignInExternalAsync(Login("first@example.com", verified: false))).Error);
        Assert.Equal(ExternalSignInError.NoVerifiedEmail, (await accounts.SignInExternalAsync(Login("bob@example.com", verified: false))).Error);
        Assert.Single(await accounts.ListUsersAsync());
    }

    [Fact]
    public async Task A_trusted_provider_needs_no_verified_flag()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        Oidc.TrustEmail = true;

        var signedIn = await accounts.SignInExternalAsync(Login("first@example.com", verified: false));

        Assert.NotNull(signedIn.User);
    }

    [Fact]
    public async Task A_subject_from_another_issuer_doesnt_sign_into_the_old_account()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        await accounts.SignInExternalAsync(Login("first@example.com", subject: "42"));

        var other = await accounts.SignInExternalAsync(Login("bob@example.com", subject: "42", issuer: "https://other.example.com"));

        Assert.Null(other.User);
        Assert.Equal(ExternalSignInError.NotInvited, other.Error);
        Assert.Equal("bob@example.com", other.Email);
    }

    [Fact]
    public async Task The_first_login_becomes_admin()
    {
        var signedIn = await accounts.SignInExternalAsync(Login("first@example.com"));

        Assert.NotNull(signedIn.User);
        Assert.True(await IsAdminAsync("first@example.com"));
    }

    [Fact]
    public async Task An_uninvited_login_gets_no_account()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);

        var signedIn = await accounts.SignInExternalAsync(Login("bob@example.com"));

        Assert.Equal(ExternalSignInError.NotInvited, signedIn.Error);
        Assert.Null(await FindUserAsync("bob@example.com"));
    }

    [Fact]
    public async Task An_invited_login_gets_an_account_and_uses_up_the_invite()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        var token = await accounts.CreateInvitationAsync("bob@example.com", null);

        var signedIn = await accounts.SignInExternalAsync(Login("Bob@Example.com"));

        Assert.NotNull(signedIn.User);
        Assert.False(await IsAdminAsync("bob@example.com"));
        Assert.Null(await accounts.FindInvitationAsync(token));
    }

    [Fact]
    public async Task Auto_provisioning_gives_any_verified_login_an_account()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        Oidc.AutoProvision = true;

        var signedIn = await accounts.SignInExternalAsync(Login("bob@example.com"));

        Assert.NotNull(signedIn.User);
        Assert.False(await IsAdminAsync("bob@example.com"));
    }

    [Fact]
    public async Task The_admin_group_grants_and_removes_the_role_on_each_login()
    {
        await accounts.RegisterAsync("first@example.com", Password, null);
        await accounts.CreateInvitationAsync("bob@example.com", null);
        Oidc.AdminGroup = "lantrn-admins";

        await accounts.SignInExternalAsync(Login("bob@example.com", groups: ["staff", "lantrn-admins"]));
        Assert.True(await IsAdminAsync("bob@example.com"));

        await accounts.SignInExternalAsync(Login("bob@example.com", groups: ["staff"]));
        Assert.False(await IsAdminAsync("bob@example.com"));
    }

    [Fact]
    public async Task The_admin_group_never_removes_the_last_admin()
    {
        Oidc.AdminGroup = "lantrn-admins";

        await accounts.SignInExternalAsync(Login("first@example.com"));
        var again = await accounts.SignInExternalAsync(Login("first@example.com"));

        Assert.NotNull(again.User);
        Assert.True(await IsAdminAsync("first@example.com"));
    }

    private OidcOptions Oidc => services.GetRequiredService<IOptions<OidcOptions>>().Value;

    // By default the subject comes from the address, so the same person signing in again presents the same login.
    private static ExternalLoginInfo Login(
        string email, bool verified = true, string[]? groups = null, string issuer = "https://provider.example.com", string? subject = null)
    {
        subject ??= $"subject-{email.ToLowerInvariant()}";
        var claims = new List<Claim>
        {
            new("sub", subject, ClaimValueTypes.String, issuer),
            new("email", email),
            new("email_verified", verified ? "True" : "False"),
        };
        claims.AddRange((groups ?? []).Select(group => new Claim(OidcOptions.GroupsClaim, group)));
        return new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(claims, OidcOptions.Scheme)), OidcOptions.Scheme, subject, "SSO");
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<bool> IsAdminAsync(string email)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"No user {email}.");
        return await users.IsInRoleAsync(user, Roles.Admin);
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await keepAlive.DisposeAsync();
    }
}
