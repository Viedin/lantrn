namespace Lantrn.Services.Accounts;

public sealed class OidcOptions
{
    public const string SectionName = "Oidc";
    public const string Scheme = "oidc";
    public const string GroupsClaim = "groups";

    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string DisplayName { get; set; } = "Sign in with SSO";
    public string Scope { get; set; } = "openid profile email";
    public bool AutoProvision { get; set; }
    // For providers that vouch for every address but don't send email_verified, such as Entra ID.
    public bool TrustEmail { get; set; }
    public string? AdminGroup { get; set; }
    public bool DisableLocalLogin { get; set; }

    public bool Enabled => !string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(ClientId);

    // Without a provider to sign in with, turning off passwords would lock everyone out.
    public bool LocalLogin => !Enabled || !DisableLocalLogin;
}
