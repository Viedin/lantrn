# Users and access

## Roles

| Role  | Can                                                                          |
| ----- | ---------------------------------------------------------------------------- |
| Admin | Do everything: every collection, settings, users, API keys                   |
| User  | Create collections, and add or remove documents in their own and public ones |
| Guest | Search public collections without signing in, if public search is on         |

The first registered account is the admin, and there is always at least one.

## Collections

Private collections are visible only to their owner and admins. Public collections can be searched and edited by every user. Only the owner and admins can change visibility, re-embed, clear or delete a collection.

Removing a user deletes the collections they own and the documents they added elsewhere. Removing an admin hands their content to the admin who removed them instead.

## Inviting users

Registration is invite-only after the first account. Create an invite under **Admin → Users** and send the link yourself, since Lantrn doesn't send email. Invites expire after 7 days and work once.

With [single sign-on](./sso), invited users can sign in through the provider instead of picking a password.

## Public search

**Settings → Public search** lets anyone search and read public collections without an account, including the search assistant if it's on. Off by default.
