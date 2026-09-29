# Development

## Run it locally

You need the **.NET 11 SDK** and a Qdrant instance.

```sh
docker run -p 6334:6334 qdrant/qdrant
cd Lantrn
dotnet run
```

Data goes to `Lantrn/lantrn.db` and `Lantrn/data/`. `appsettings.Development.json` points the models at LM Studio on `localhost:1234`.

To run your changes in Docker, build the image from source instead of pulling the published one:

```sh
cd Lantrn
docker compose up -d --build
```

## Releases

Publishing a GitHub release builds the Docker image for amd64 and arm64 and pushes it to `ghcr.io/viedin/lantrn` (see `.github/workflows/docker.yml`). Tag releases as `v1.2.3`: that publishes `1.2.3`, `1.2` and `latest`. A prerelease only gets its exact tag.

## Stack

- **Blazor Server** (interactive server rendering)
- **SQLite** + EF Core for users, settings, collections and document text
- **Qdrant** for vectors (dense + sparse keyword vectors)
- **Xberg** for text extraction and chunking
- **OpenAI SDK** for any OpenAI-compatible endpoint

## Project layout

```
Lantrn/
├── Program.cs
├── Startup/          # DI and auth setup
├── Api/              # Public REST API
├── Components/
│   ├── Public/       # Search and document view (search access)
│   ├── Pages/        # Collections, ingest and crawl (signed in)
│   ├── Admin/        # Dashboard, users, settings, API keys (admin only)
│   ├── Account/      # Login and register
│   ├── Search/       # Search UI parts
│   └── Shared/       # Reusable components
├── Services/         # All the logic
└── Infra/            # EF entities, DbContext, migrations
```

Access is set per folder in `_Imports.razor`: `Public/` needs search access, `Pages/` needs an account, `Admin/` needs the Admin role. Which collections someone can read or edit is decided in `CollectionAccess`.

## Key services

| Service                       | Does                                                          |
| ----------------------------- | ------------------------------------------------------------- |
| `DocumentExtractor`           | Files → markdown → chunks                                     |
| `EmbeddingService`            | Chunks and queries → vectors                                  |
| `VisionOcrService`            | Images → markdown via the vision model                        |
| `QdrantStore`                 | Writes points, runs hybrid search (Qdrant's BM25 for keywords) |
| `SearchService`               | Search → optional rerank → drops overlapping hits             |
| `Reranker`                    | Reorders candidates through a `/rerank` endpoint              |
| `DocumentStore`               | Collections, documents, tags. Keeps SQLite and Qdrant in step |
| `IngestQueue`, `IngestWorker` | Background queue that ingests one job at a time               |
| `SourceSyncService`           | Re-crawls websites on their schedule                          |
| `WebCrawler`                  | Finds and fetches pages for a crawl                           |
| `SearchAssistant`             | Answers from search results with citations                    |
| `SettingsStore`               | In-memory copy of the settings row                            |
| `AccountService`              | Users, roles and invites                                      |

## How ingest works

1. Extract the file to markdown (Xberg, or the vision model for images).
2. Split into chunks at headings, respecting chunk size and overlap.
3. Embed each chunk and build its keyword vector.
4. Write the new points to Qdrant, then remove the old ones — a failure halfway leaves the previous version searchable.
5. Save the markdown, tags and (for PDFs and images) the original file.

## Link extractors

A link on the Crawl page is crawled as a website unless an `ILinkExtractor` in `Services/Ingestion/Links` handles it, like `GitHubExtractor`. To add one, implement the interface and register it in `CoreServices`.

## Database migrations

Never write migrations by hand. Use the EF tool:

```sh
cd Lantrn
dotnet ef migrations add <Name>
```

Migrations run automatically on startup.

## Docs

These docs live in `Lantrn-docs/` and use [VitePress](https://vitepress.dev).

```sh
cd Lantrn-docs
npm install
npm run docs:dev
```
