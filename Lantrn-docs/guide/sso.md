# Single sign-on (OIDC)

Lantrn can sign people in through an OpenID Connect provider, such as Authentik, Keycloak, Authelia, Pocket ID or Entra ID. This works alongside passwords or can replace them.

In the provider, create a confidential client with:

- Redirect URI: `https://lantrn.example.com/signin-oidc`
- Post-logout redirect URI: `https://lantrn.example.com/signout-callback-oidc`

Then configure Lantrn:

| Key                       | Default                | What it is                                                     |
| ------------------------- | ---------------------- | -------------------------------------------------------------- |
| `Oidc__Authority`         | —                      | The provider's issuer URL                                      |
| `Oidc__ClientId`          | —                      |                                                                |
| `Oidc__ClientSecret`      | —                      |                                                                |
| `Oidc__DisplayName`       | `Sign in with SSO`     | Text on the sign-in button                                     |
| `Oidc__Scope`             | `openid profile email` |                                                                |
| `Oidc__AutoProvision`     | `false`                | Give anyone the provider lets in an account, without an invite |
| `Oidc__TrustEmail`        | `false`                | Accept emails the provider doesn't mark as verified            |
| `Oidc__AdminGroup`        | —                      | Members of this group are admins                               |
| `Oidc__DisableLocalLogin` | `false`                | Hide password sign-in and registration                         |

Signing out of Lantrn also signs out of the provider, so the next person at the computer isn't let in as you.

## Who gets in

The first time someone signs in, Lantrn links them to the account with the same email, even an admin's. If there is no such account, they need a pending [invite](./users#inviting-users) for that email, or auto-provisioning. On a fresh install, the first person to sign in becomes the admin.

::: warning Lantrn trusts the provider's email
Anyone who can get your provider to vouch for an address can sign in as that account. The provider must mark the email as verified, unless `Oidc__TrustEmail` is on. Only turn that on if users can't change their own address at the provider.
:::

## Admin group

When `Oidc__AdminGroup` is set, the `groups` claim decides the admin role on every sign-in, except that the last admin is never demoted. Some providers only send groups when asked:

- **Authelia** and **Pocket ID**: set `Oidc__Scope` to `openid profile email groups`.
- **Keycloak**: add a *Group Membership* mapper named `groups` to the client. Groups arrive as paths like `/admins` unless *Full group path* is off.
- **Entra ID**: add a groups claim under *Token configuration*. It holds group object IDs, so use the ID as the admin group.

## Behind a reverse proxy

Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"` so Lantrn sees the original `https` address. Without it, the redirect URI it sends starts with `http://` and the provider rejects it.

## Example: Authentik

1. Create an **OAuth2/OpenID Provider** with the client type *Confidential* and the redirect URIs above.
2. Create an **Application** with the slug `lantrn` that uses it.
3. Add this to `docker-compose.yml`:

```yaml
environment:
  Oidc__Authority: https://authentik.example.com/application/o/lantrn/
  Oidc__ClientId: <client id>
  Oidc__ClientSecret: <client secret>
  Oidc__DisplayName: Sign in with Authentik
  Oidc__AdminGroup: lantrn-admins
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"
```

## Example: Entra ID

Entra doesn't say whether an email is verified, so Lantrn has to trust it.

1. Register an app with the redirect URIs above as a *Web* platform, and create a client secret.
2. Under *Token configuration*, add the optional claim `email` to the ID token.
3. Add this to `docker-compose.yml`:

```yaml
environment:
  Oidc__Authority: https://login.microsoftonline.com/<tenant id>/v2.0
  Oidc__ClientId: <application id>
  Oidc__ClientSecret: <client secret>
  Oidc__DisplayName: Sign in with Microsoft
  Oidc__TrustEmail: "true"
```
