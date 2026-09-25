# Lantrn

Lantrn is a self-hosted search app for your documents. Upload files or crawl a website, and Lantrn indexes them so you can search by meaning, not just exact words. It can also have an LLM answer your question based on what the search found.

It's built with Blazor Server, uses [Qdrant](https://qdrant.tech) for vectors and [Xberg](https://www.nuget.org/packages/XbergIo.Xberg) for text extraction, and works with any OpenAI-compatible API (OpenAI, LM Studio, Ollama, vLLM and so on).

**Documentation: [viedin.github.io/lantrn](https://viedin.github.io/lantrn/)**

## Features

- Hybrid search: embeddings combined with keyword matching
- PDF, Markdown, HTML, plain text and images (OCR through a vision model)
- Website crawling
- Collections and tags to keep things organized
- Optional LLM answers based on your search results
- Users, admins and invites

## Running it

With Docker, using the published image from `ghcr.io/viedin/lantrn`:

```sh
curl -O https://raw.githubusercontent.com/Viedin/lantrn/main/Lantrn/docker-compose.yml
docker compose up -d
```

To build the image from source instead, run `docker compose up -d --build` from the `Lantrn/` folder.

Then open http://localhost:8080. The first account you register becomes the admin. After that, new users need an invite.

Set up your embedding endpoint (and optionally a chat and vision model) on the Settings page. You can also set them through environment variables in `docker-compose.yml`.

## Development

You need the .NET 11 SDK and a Qdrant instance:

```sh
docker run -p 6334:6334 qdrant/qdrant
cd Lantrn
dotnet run
```

Data is stored in `Lantrn/lantrn.db` (SQLite) and the `Lantrn/data/` folder. The Qdrant connection is configured under `Qdrant` in `Lantrn/appsettings.json`.

Migrations are created with `dotnet ef migrations add <Name>` (from `Lantrn/`) and run automatically when the app starts.

## Documentation

The docs are published at [viedin.github.io/lantrn](https://viedin.github.io/lantrn/). The source lives in `Lantrn-docs/` and is built with [VitePress](https://vitepress.dev). Pushes to `main` redeploy it automatically. To work on the docs locally:

```sh
cd Lantrn-docs
npm install
npm run docs:dev
```

## License

Lantrn is licensed under the [GNU Affero General Public License v3.0](LICENSE). You're free to use, modify and self-host it. If you run a modified version that other people use over a network, you must make your source code available to them under the same license.

If you want to use Lantrn without the AGPL obligations, for example in a closed-source product, commercial licenses are available. Contact filipviedin@gmail.com.

## Contributing

Pull requests are welcome. Because Lantrn is also offered under a commercial license, contributors must sign a Contributor License Agreement before their changes can be merged.
