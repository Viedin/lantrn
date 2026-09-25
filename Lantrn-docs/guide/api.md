# Public API

The API lets scripts and integrations do what an admin does in **Collections** and **Ingest**: create and delete collections, add documents and manage the ones already there.

The full reference lives in Lantrn itself at **Developers → API reference** (`/api/reference`). It is generated from the OpenAPI document at `/api/openapi/v1.json`, which you can also load into Postman, Insomnia or a client generator.

## API keys

Every request needs an API key from an admin.

1. Go to **Developers → API keys**.
2. Name the key after what will use it, and create it.
3. Copy the key. It is shown only once.

Send it as a bearer token:

```sh
curl https://lantrn.example.com/api/v1/collections \
  -H "Authorization: Bearer $LANTRN_API_KEY"
```

A key acts as the admin who created it. It stops working if you revoke it, or if that user stops being an admin or is removed.

## Ingesting documents

There are three ways to add a document to a collection:

| Endpoint                                      | Sends                                  |
| --------------------------------------------- | -------------------------------------- |
| `POST /api/v1/collections/{name}/documents`          | A file, as `multipart/form-data`       |
| `POST /api/v1/collections/{name}/documents/markdown` | Markdown or plain text, as JSON        |
| `POST /api/v1/collections/{name}/documents/url`      | A URL for Lantrn to fetch, as JSON     |

Each one waits until the document is extracted, chunked and embedded, and then returns the stored document. Like on the Ingest page, a document with the same source (file name or URL) in the collection is replaced, not duplicated.

```sh
curl https://lantrn.example.com/api/v1/collections/handbook/documents \
  -H "Authorization: Bearer $LANTRN_API_KEY" \
  -F "file=@leave-policy.pdf" \
  -F "tags=hr,policies"
```

## Errors

Errors are returned as [problem details](https://www.rfc-editor.org/rfc/rfc9457) JSON with a matching status code, such as `404` for an unknown collection or `409` when a collection already exists.
