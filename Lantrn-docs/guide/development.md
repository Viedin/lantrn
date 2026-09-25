# Development

## Run it locally

You need the **.NET 11 SDK** and a Qdrant instance.

```sh
docker run -p 6334:6334 qdrant/qdrant
cd Lantrn
dotnet run
```

Data goes to `Lantrn/lantrn.db` and `Lantrn/data/`. `appsettings.Development.json` points the models at LM Studio on `localhost:1234`.

## Stack

- **Blazor Server** (interactive server rendering)
- **SQLite** + EF Core for users, settings, collections and document text
- **Qdrant** for vectors (dense + sparse keyword vectors)
- **Xberg** for text extraction and chunking
- **OpenAI SDK** for any OpenAI-compatible endpoint

## Project layout

```
Lantrn/
├── Program.cs        # Startup and DI
├── Components/
│   ├── Public/       # Search and document view (search access)
│   ├── Pages/        # Admin pages (admin only)
│   ├── Account/      # Login and register
│   ├── Search/       # Search UI parts
│   └── Shared/       # Reusable components
├── Services/         # All the logic
└── Infra/            # EF entities, DbContext, migrations
```

Access is set per folder in `_Imports.razor`: `Public/` needs search access, `Pages/` needs the Admin role.

## Key services

| Service                 | Does                                                          |
| ----------------------- | ------------------------------------------------------------- |
| `DocumentExtractor`     | Files → markdown → chunks                                     |
| `EmbeddingService`      | Chunks and queries → vectors                                  |
| `VisionOcrService`      | Images → markdown via the vision model                        |
| `KeywordEncoder`        | Text → sparse keyword vector (BM25-style)                     |
| `QdrantStore`           | Writes points, runs hybrid search                             |
| `DocumentStore`         | Collections, documents, tags. Keeps SQLite and Qdrant in step |
| `DocumentFolderWatcher` | Syncs the documents folder every minute                       |
| `WebCrawler`            | Finds and fetches pages for a crawl                           |
| `SearchAssistant`       | Answers from search results with citations                    |
| `SettingsStore`         | In-memory copy of the settings row                            |
| `AccountService`        | Users, roles and invites                                      |

## How ingest works

1. Extract the file to markdown (Xberg, or the vision model for images).
2. Split into chunks at headings, respecting chunk size and overlap.
3. Embed each chunk and build its keyword vector.
4. Write the new points to Qdrant, then remove the old ones — a failure halfway leaves the previous version searchable.
5. Save the markdown, tags and (for PDFs and images) the original file.

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
