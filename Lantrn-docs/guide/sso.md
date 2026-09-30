# Single sign-on (OIDC)

Lantrn can sign people in through an OpenID Connect provider such as Authentik, Keycloak, Authelia, Pocket ID or Entra ID, alongside or instead of passwords.

Create a confidential client in the provider with these redirect URIs:

- `https://lantrn.example.com/signin-oidc`
- `https://lantrn.example.com/signout-callback-oidc` (post-logout)

Then set:

| Key                       | Default                | What it is                                                     |
| ------------------------- | ---------------------- | -------------------------------------------------------------- |
| `Oidc__Authority`         |                        | The provider's issuer URL                                      |
| `Oidc__ClientId`          |                        |                                                                |
| `Oidc__ClientSecret`      |                        |                                                                |
| `Oidc__DisplayName`       | `Sign in with SSO`     | Text on the sign-in button                                     |
| `Oidc__Scope`             | `openid profile email` |                                                                |
| `Oidc__AutoProvision`     | `false`                | Give anyone the provider lets in an account, without an invite |
| `Oidc__TrustEmail`        | `false`                | Accept emails the provider doesn't mark as verified            |
| `Oidc__AdminGroup`        |                        | Members of this group are admins                               |
| `Oidc__DisableLocalLogin` | `false`                | Hide password sign-in and registration                         |

Behind a reverse proxy, also set `ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"`, or the redirect URI is sent as `http://` and the provider rejects it.

## Who gets in

On first sign-in, Lantrn links the person to the account with the same email, admins included. Without one, they need a pending [invite](./users#inviting-users) or `Oidc__AutoProvision`. On a fresh install, the first person to sign in becomes the admin.

::: warning
Because accounts are matched by email, the provider must only vouch for addresses users actually own. Only turn on `Oidc__TrustEmail` if users can't change their own email at the provider.
:::

## Admin group

With `Oidc__AdminGroup` set, the `groups` claim decides the admin role on every sign-in. The last admin is never demoted.

- **Authelia**, **Pocket ID**: add `groups` to `Oidc__Scope`.
- **Keycloak**: add a *Group Membership* mapper named `groups`. Groups arrive as paths like `/admins` unless *Full group path* is off.
- **Entra ID**: add a groups claim under *Token configuration* and use the group's object ID.

## Entra ID

Entra doesn't mark emails as verified, so set `Oidc__TrustEmail: "true"` and add the optional `email` claim to the ID token. The authority is `https://login.microsoftonline.com/<tenant id>/v2.0`.
