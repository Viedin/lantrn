# Getting started

Lantrn runs as two containers: the app and [Qdrant](https://qdrant.tech), which stores the vectors.

## 1. Start it

From the `Lantrn/` folder:

```sh
docker compose up -d
```

Open http://localhost:8080.

## 2. Create the admin account

The **first account you register becomes the admin**. After that, registration is closed and new users need an [invite](./users).

## 3. Connect an embedding model

Search needs an embedding model. Go to **Settings** and fill in the **Embeddings** section:

| Field    | Example                                 |
| -------- | --------------------------------------- |
| Base URL | `http://host.docker.internal:1234/v1`   |
| Model    | `text-embedding-qwen3-embedding-4b`     |
| API key  | Only if your endpoint needs one         |

The base URL must include the version segment (`/v1`).

::: tip Running a model on your own machine?
From inside Docker, `localhost` is the container itself. Use `host.docker.internal` to reach LM Studio or Ollama on the host.
:::

A **vision** model (for images) and a **chat** model (for answers) are optional. See [Configuration](./configuration).

## 4. Add documents

The quickest way: drop files into `Lantrn/documents/` next to `docker-compose.yml`. Lantrn picks them up within a minute.

Or upload them on the **Ingest** page. See [Adding documents](./adding-documents) for all the options.

## 5. Search

Go to the home page and type a question. That's it.
