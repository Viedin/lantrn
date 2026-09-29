# Configuration

Most things are set on the **Settings** page and take effect on the next search or ingest — no restart needed.

Any OpenAI-compatible server works: OpenAI, LM Studio, Ollama, vLLM and so on.

## Embeddings

Required. Turns text into vectors for search. The dashboard shows a reminder until one works.

- The base URL must include `/v1`.
- **Dimensions** is only sent if set. Many local models reject it.
- **Query prefix** is prepended to search queries only. Instruction-tuned models like Qwen3-Embedding expect one:

```
Instruct: Given a search query, retrieve relevant passages that answer the query
Query: 
```

::: warning Changing the model or keyword language
Vectors from different models don't match. If you switch embedding model or keyword language, use **Re-embed** on each collection's page.
:::

## Vision

Optional. Reads text from images. Needed only if you ingest images.

## Search assistant

Optional, **off** by default. A chat model that answers from the top search results.

## Reranker

Optional, **off** by default. Reorders the top search results with a reranker model such as `bge-reranker-v2-m3` or `Qwen3-Reranker`. The server must offer a Cohere/Jina-style `/rerank` endpoint. vLLM, llama.cpp (`--reranking`), Infinity, Jina and Cohere do; LM Studio and Ollama don't.

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
  Reranker__Enabled: "true"
  Reranker__BaseUrl: http://host.docker.internal:8080/v1
  Keywords__Language: english
  Access__PublicSearch: "false"
```

## Server settings

These are only set through configuration, not the Settings page.

| Key                         | Default                 | What it is                                |
| --------------------------- | ----------------------- | ----------------------------------------- |
| `ConnectionStrings__Lantrn` | `Data Source=lantrn.db` | SQLite database                           |
| `Storage__DataPath`         | `data`                  | Originals, images, keys, documents folder |
| `Qdrant__Host`              | `localhost`             |                                           |
| `Qdrant__Port`              | `6334`                  | gRPC port                                 |
| `Qdrant__Https`             | `false`                 |                                           |
| `Qdrant__ApiKey`            | —                       |                                           |

## Backups

Everything lives in two places:

- The **app data volume** (`/app/data`): database, uploaded originals, sign-in keys.
- The **Qdrant volume**: the vectors.

Back up both. If you lose only Qdrant, **Re-embed** on each collection rebuilds it.
