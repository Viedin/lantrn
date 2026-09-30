# Development

## Run it locally

You need the .NET 11 SDK and Qdrant:

```sh
docker run -p 6334:6334 qdrant/qdrant
cd Lantrn
dotnet run
```

Data goes to `Lantrn/lantrn.db` and `Lantrn/data/`. `appsettings.Development.json` points the models at LM Studio on `localhost:1234`.

To run your changes in Docker, use `docker compose up -d --build` from `Lantrn/`.

## Stack

Blazor Server, SQLite with EF Core, Qdrant for dense and keyword vectors, Xberg for text extraction and chunking, and the OpenAI SDK for model endpoints.

## Project layout

```
Lantrn/
├── Startup/          # DI and auth setup
├── Api/              # REST API and MCP tools
├── Components/
│   ├── Public/       # Search and document view
│   ├── Pages/        # Collections, ingest and crawl
│   ├── Admin/        # Dashboard, users, settings, API keys
│   ├── Account/      # Login and register
│   ├── Search/       # Search UI parts
│   └── Shared/       # Reusable components
├── Services/         # The logic
└── Infra/            # EF entities, DbContext, migrations
```

Access is set per folder in `_Imports.razor`. Which collections someone can read or edit is decided in `CollectionAccess`.

New link types for the Crawl page (like `GitHubExtractor`) implement `ILinkExtractor` and are registered in `CoreServices`.

## Migrations

Don't write migrations by hand:

```sh
cd Lantrn
dotnet ef migrations add <Name>
```

They run automatically on startup.

## Releases

Publishing a GitHub release tagged `v1.2.3` builds the image for amd64 and arm64 and pushes `1.2.3`, `1.2` and `latest` to `ghcr.io/viedin/lantrn`. Prereleases only get their exact tag.

## Docs

```sh
cd Lantrn-docs
npm install
npm run docs:dev
```

Pushes to `main` redeploy them to GitHub Pages.
