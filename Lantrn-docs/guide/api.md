# Public API

The API covers search, collections and documents. The full reference is in the app under **Developers → API reference**, generated from `/api/openapi/v1.json`.

## API keys

Create a key under **Developers → API keys**. It's shown once. Send it as a bearer token:

```sh
curl https://lantrn.example.com/api/v1/collections \
  -H "Authorization: Bearer $LANTRN_API_KEY"
```

A key acts as the admin who created it, and stops working if it's revoked or that user is no longer an admin.

## Endpoints

| Endpoint                                           | Does                               |
| -------------------------------------------------- | ---------------------------------- |
| `GET /api/v1/collections/{id}/search?q=...`        | Search one collection              |
| `GET /api/v1/documents/{id}`                       | Get a full document                |
| `POST /api/v1/collections/{id}/documents`          | Add a file (`multipart/form-data`) |
| `POST /api/v1/collections/{id}/documents/markdown` | Add Markdown or text (JSON)        |
| `POST /api/v1/collections/{id}/documents/url`      | Add a URL for Lantrn to fetch      |

Adding a document waits until it's embedded and returns it. A document with the same file name or URL replaces the old one. Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457) JSON.
