# Lantrn

Self-hosted semantic search for your documents. Upload files or crawl a website, then search them by meaning. Optionally, a chat model answers from the results.

Built with Blazor Server, [Qdrant](https://qdrant.tech) and [Xberg](https://www.nuget.org/packages/XbergIo.Xberg). Works with any OpenAI-compatible model server (OpenAI, LM Studio, Ollama, vLLM).

**Docs: [viedin.github.io/lantrn](https://viedin.github.io/lantrn/)**

## Features

- Hybrid search: embeddings plus keyword matching
- PDF, Office, e-mail, e-books, Markdown, HTML, text, and images through a vision model
- Website and GitHub repository crawling, with scheduled re-syncs
- Collections and tags
- Optional LLM answers with citations
- Users, invites, private and public collections, OIDC single sign-on
- REST API and MCP server

## Quick start

```sh
curl -O https://raw.githubusercontent.com/Viedin/lantrn/main/Lantrn/docker-compose.yml
docker compose up -d
```

Open http://localhost:8080 and register. The first account becomes the admin. Then set your embedding model under **Settings**.

See the [docs](https://viedin.github.io/lantrn/) for configuration, SSO and [development](https://viedin.github.io/lantrn/guide/development).

## Contributing

Pull requests are welcome.

## License

[MIT](LICENSE)
