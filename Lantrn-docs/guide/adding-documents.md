# Adding documents

There are three ways to get documents into Lantrn. All of them split documents into chunks, embed them and store them in a **collection**.

## Supported files

| Type       | Extensions                                  |
| ---------- | ------------------------------------------- |
| PDF        | `.pdf`                                      |
| Markdown   | `.md`, `.markdown`                          |
| Text       | `.txt`                                      |
| HTML       | `.html`, `.htm`                             |
| Images     | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`    |

Images are read by the **vision model** (not Tesseract), so photos of receipts or whiteboards work too. You need a vision model set up in [Settings](./configuration#vision) to ingest images.

## Upload

**Ingest** page (admins only). Pick files, a collection and optional tags, then upload.

## Documents folder

Anything in the documents folder is synced into the default `documents` collection:

- New and changed files are ingested (changes are detected by content hash).
- Deleted files are removed from the collection.
- The folder is checked **once a minute**. Subfolders are included.
- Files over 100 MB are skipped.

With Docker Compose the folder is `Lantrn/documents/`. Locally it is `data/documents/`.

## Crawl a website

**Crawl** page (admins only). Give it a start URL and Lantrn follows the sitemap and links.

| Field             | What it does                                                   |
| ----------------- | -------------------------------------------------------------- |
| Start URL         | Where the crawl begins. Always counts as one page.             |
| Only pages under  | Limits the crawl to one path, e.g. `https://example.com/docs/` |
| Max pages         | The most pages to store. Default 100.                          |

The crawler honours `robots.txt`, strips navigation and footers, and refuses private or local network addresses.

## Collections and tags

- **Collections** are separate indexes. Search one at a time. Names are lowercase letters, digits, `-` and `_`.
- The `documents` collection always exists. It can be cleared but not deleted.
- **Tags** are free-form labels (comma separated). You can filter search results by tag.

## Chunking

The ingest and crawl pages let you tune how documents are split. The defaults work fine for most cases.

| Setting        | Default | What it does                                                                 |
| -------------- | ------- | ---------------------------------------------------------------------------- |
| Chunk size     | 2000    | Max characters per chunk. Split at headings first, so most are shorter.      |
| Min chunk size | 100     | Drop chunks shorter than this (e.g. a lone heading). `0` keeps everything.   |
| Overlap        | 200     | Characters repeated from the previous chunk, so split sentences are still found. |

Smaller chunks give more precise hits. Larger chunks keep more context together.
