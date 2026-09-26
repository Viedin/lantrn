# Configuration

Most things are set on the **Settings** page and take effect on the next search or ingest — no restart needed.

Any OpenAI-compatible server works: OpenAI, LM Studio, Ollama, vLLM and so on.

## Embeddings

Required. Turns text into vectors for search. A fresh install has no endpoint set, and the dashboard shows a reminder until one works.

| Field        | Default                       | Notes                                                          |
| ------------ | ----------------------------- | -------------------------------------------------------------- |
| Base URL     | —                             | Include `/v1`                                                  |
| Model        | —                             | Picked from the models the endpoint lists                      |
| API key      | —                             |                                                                |
| Dimensions   | —                             | Only sent if set. Many local models reject it.                 |
| Batch size   | 32                            | Chunks per request                                             |
| Timeout (s)  | 600                           |                                                                |
| Query prefix | —                             | Prepended to search queries only. See below.                   |

::: warning Changing the model
Vectors from different models don't match. If you switch embedding model, **re-ingest** your collections.
:::

**Query prefix:** instruction-tuned models like Qwen3-Embedding expect a task instruction on queries:

```
Instruct: Given a search query, retrieve relevant passages that answer the query
Query: 
```

## Vision

Optional. Reads images (OCR). Needed only if you ingest images.

| Field       | Default          |
| ----------- | ---------------- |
| Base URL    | —                |
| Model       | —                |
| Timeout (s) | 600              |

## Search assistant

Optional, **off** by default. A chat model that answers from the search results.

| Field       | Default          | Notes                              |
| ----------- | ---------------- | ---------------------------------- |
| Base URL    | —                |                                    |
| Model       | —                |                                    |
| Sources     | 6                | How many top results it reads      |
| Timeout (s) | 120              |                                    |

## Setting defaults with environment variables

On a fresh install, the settings start from environment variables (or `appsettings.json`). After the first start they live in the database and the Settings page wins.

Use `__` (double underscore) for nesting:

```yaml
# docker-compose.yml
environment:
  Embeddings__BaseUrl: http://host.docker.internal:1234/v1
  Embeddings__Model: text-embedding-qwen3-embedding-4b
  Vision__Model: qwen2.5-vl-7b-instruct
  Assistant__Enabled: "true"
  Access__PublicSearch: "false"
```

## Server settings

These are only set through configuration, not the Settings page.

| Key                         | Default                 | What it is                               |
| --------------------------- | ----------------------- | ---------------------------------------- |
| `ConnectionStrings__Lantrn` | `Data Source=lantrn.db` | SQLite database                          |
| `Storage__DataPath`         | `data`                  | Originals, images, keys, documents folder |
| `Qdrant__Host`              | `localhost`             |                                          |
| `Qdrant__Port`          | `6334`               | gRPC port                                |
| `Qdrant__Https`         | `false`              |                                          |
| `Qdrant__ApiKey`        | —                    |                                          |

## Backups

Everything lives in two places:

- The **app data volume** (`/app/data`): database, uploaded originals, sign-in keys.
- The **Qdrant volume**: the vectors.

Back up both. If you lose only Qdrant, re-ingesting rebuilds it.
