# Configuration

Models and endpoints are set on the **Settings** page and apply without a restart. Any OpenAI-compatible server works.

## Embeddings

Required.

- **Dimensions** is only sent when set. Many local models reject it.
- **Query prefix** is added to search queries only. Instruction-tuned models like Qwen3-Embedding expect one, e.g. `Instruct: Given a search query, retrieve relevant passages that answer the query\nQuery: `.

::: warning
Vectors from different models don't mix. After switching embedding model or keyword language, use **Re-embed** on each collection.
:::

## Vision

Optional. Reads text from images, so it's only needed if you ingest images.

## Search assistant

Optional, off by default. A chat model that answers from the top search results. It sends document text to the chat endpoint, so use a local model if that matters to you.

## Reranker

Optional, off by default. Reorders the top results with a reranker model such as `bge-reranker-v2-m3`. The server needs a Cohere/Jina-style `/rerank` endpoint: vLLM, llama.cpp (`--reranking`), Infinity, Jina and Cohere have one, LM Studio and Ollama don't.

## Environment variables

On a fresh install the settings are seeded from environment variables. After the first start they live in the database and the Settings page wins. Use `__` for nesting:

```yaml
environment:
  Embeddings__BaseUrl: http://host.docker.internal:1234/v1
  Embeddings__Model: text-embedding-qwen3-embedding-4b
  Assistant__Enabled: "true"
```

These can only be set through configuration:

| Key                         | Default                 | What it is                            |
| --------------------------- | ----------------------- | ------------------------------------- |
| `ConnectionStrings__Lantrn` | `Data Source=lantrn.db` | SQLite database                       |
| `Storage__DataPath`         | `data`                  | Originals, images, keys, upload inbox |
| `Qdrant__Host`              | `localhost`             |                                       |
| `Qdrant__Port`              | `6334`                  | gRPC port                             |
| `Qdrant__Https`             | `false`                 |                                       |
| `Qdrant__ApiKey`            |                         |                                       |

[Single sign-on](./sso) has its own settings.

## Reverse proxy

Behind a proxy that terminates HTTPS, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"` so Lantrn sees the original address. The proxy must also pass WebSockets through, since the UI runs over a live connection.

## Backups

Back up both Docker volumes: the app data volume (`/app/data`, with the database, originals and sign-in keys) and the Qdrant volume. If you only lose Qdrant, **Re-embed** on each collection rebuilds it.
