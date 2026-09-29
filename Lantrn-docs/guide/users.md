# Users and access

## Roles

| Role   | Can do                                                                          |
| ------ | ------------------------------------------------------------------------------- |
| Admin  | Everything: every collection, settings, users, API keys                         |
| User   | Create collections; add and remove documents in their own and in public ones     |
| Guest  | Not signed in. Search public collections, only while public search is on        |

The first account ever registered becomes the admin. There must always be at least one admin.

## Public and private collections

Each collection is public or private. Private collections can only be seen by their owner and the admins. Public collections are shared: every user can search them and add, retag or remove their documents. Only the owner and the admins can change a collection's visibility, re-embed, clear or delete it.

Names are unique per owner, so two users can each have a collection called `notes`. Collections synced from the documents folder are public and managed by the admins. When a user is removed, their collections stay and the admins take them over.

## Inviting users

Registration is invite-only after the first account.

1. Go to **Admin → Users**.
2. Enter an email and create an invite.
3. Send the invite link to the person. Lantrn doesn't send email.

Invites expire after **7 days** and can be used once. Creating a new invite for the same email replaces the old one.

## Public search

In **Settings → Public search** you can let anyone search and read public collections without signing in. It is **off** by default.

When it's on, visitors can also use the search assistant if that is enabled.

## Changing roles

Admins can promote or demote users, or remove them, from **Admin → Users**. Changes take effect on open pages within a short while — no need for the user to sign out.
