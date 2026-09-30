# Getting started

Lantrn runs as two containers: the app and [Qdrant](https://qdrant.tech) for the vectors.

## Install

Download the compose file into a new folder and start it:

```sh
curl -O https://raw.githubusercontent.com/Viedin/lantrn/main/Lantrn/docker-compose.yml
docker compose up -d
```

Open http://localhost:8080 and register. The first account becomes the admin. After that, new users need an [invite](./users#inviting-users).

## Connect an embedding model

Search needs an embedding model. Fill in **Settings → Embeddings**:

| Field    | Example                               |
| -------- | ------------------------------------- |
| Base URL | `http://host.docker.internal:1234/v1` |
| Model    | `text-embedding-qwen3-embedding-4b`   |
| API key  | Only if your endpoint needs one       |

The base URL must end with the version segment (`/v1`). Inside Docker, `localhost` is the container itself, so use `host.docker.internal` to reach LM Studio or Ollama on the host.

A vision model (for images) and a chat model (for answers) are optional. See [Configuration](./configuration).

## Add documents and search

Create a collection on **Collections**, then upload files on **Ingest** or crawl a site on **Crawl**. See [Adding documents](./adding-documents).

## Updating

```sh
docker compose pull
docker compose up -d
```

Your data lives in Docker volumes and is kept. To stay on a version, pin the tag in `docker-compose.yml`, for example `ghcr.io/viedin/lantrn:1.2`.
