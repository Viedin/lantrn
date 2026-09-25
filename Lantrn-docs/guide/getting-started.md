# Getting started

Lantrn runs as two containers: the app and [Qdrant](https://qdrant.tech), which stores the vectors.

## 1. Start it

Download the compose file into a new folder and start it:

```sh
mkdir lantrn && cd lantrn
curl -O https://raw.githubusercontent.com/Viedin/lantrn/main/Lantrn/docker-compose.yml
docker compose up -d
```

This pulls the published image, `ghcr.io/viedin/lantrn`. Open http://localhost:8080.

::: tip Updating
Run `docker compose pull && docker compose up -d` to move to the latest release. Your data lives in Docker volumes and is kept. To stay on a specific version, change the tag in `docker-compose.yml`, for example `ghcr.io/viedin/lantrn:1.2`.
:::

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

The quickest way: drop files into the `documents/` folder next to `docker-compose.yml`. Lantrn picks them up within a minute.

Or upload them on the **Ingest** page. See [Adding documents](./adding-documents) for all the options.

## 5. Search

Go to the home page and type a question. That's it.
