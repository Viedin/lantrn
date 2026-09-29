# Adding documents

There are several ways to get documents into Lantrn. All of them split documents into chunks, embed them and store them in a **collection**.

Uploads, crawls and folder changes go into a **queue** that is worked through in the background. You can close the page while it runs, and anything still queued is picked up again after a restart.

## Supported files

| Type         | Extensions                                        |
| ------------ | ------------------------------------------------- |
| PDF          | `.pdf`                                            |
| Office       | `.docx`, `.pptx`, `.xlsx`, `.xls`, `.odt`, `.ods`, `.rtf` |
| E-mail       | `.eml`, `.msg`                                    |
| E-books      | `.epub`                                           |
| Markdown     | `.md`, `.markdown`                                |
| Text         | `.txt`                                            |
| HTML         | `.html`, `.htm`                                   |
| Data         | `.csv`, `.tsv`, `.json`, `.xml`, `.yaml`, `.yml`  |
| Images       | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`          |

Images are read by the **vision model** (not Tesseract), so photos of receipts or whiteboards work too. You need a vision model set up in [Settings](./configuration#vision) to ingest images.

## Upload

**Ingest** page. Users can ingest into their own collections, admins into any. Drop files on the upload area or click it to choose them, pick a collection and optional tags, then ingest.

- **ZIP archives** are unpacked: every supported file inside becomes its own document, tagged with the folders it was in. Up to 1000 files and 500 MB unpacked.
- **Paste a screenshot** anywhere on the page to add it, just like a chosen file.
- Files can be at most 100 MB.

## Documents folder

Anything in the documents folder is kept in sync. Collections it creates are public and managed by the admins.

- Files directly in the folder go into the default `documents` collection.
- Each **top-level folder becomes a collection** with the same name, created if needed. `recipes/pasta.pdf` goes into `recipes`.
- Deeper folders become **tags**: `hr/2025/leave.pdf` goes into `hr`, tagged `2025`.
- New and changed files are ingested (changes are detected by content hash). Deleted files are removed from their collection.
- Changes are usually picked up within seconds. The folder is also checked **once a minute**, for network shares that don't report changes.
- Folder names must be valid collection names (lowercase letters, digits, `-` and `_`; upper case is lowered). Other folders, hidden files and files over 100 MB are skipped.

With Docker Compose the folder is `documents/`, next to `docker-compose.yml`. Locally it is `data/documents/`.

## Crawl a website

**Crawl** page. Users can crawl into their own collections, admins into any. Give it a start URL and Lantrn follows the sitemap and links, keeping only pages under one path such as `/docs`. It honours `robots.txt`, strips navigation and footers, and refuses private or local network addresses.

A re-crawl only re-embeds pages that changed and removes pages that are gone. If it finds less than half the pages it found before, nothing is removed, so a site that is briefly down doesn't empty the collection. Removing a website stops the syncing but keeps its pages.

### GitHub repositories

Give the Crawl page a link to a public GitHub repository, such as `https://github.com/owner/repo` or `.../tree/main/src` for one folder, and Lantrn adds its code, READMEs and markdown docs, one document per file. It is kept in sync like a website. Dependencies, build output and files over 512 KB are skipped, and a repository can hold at most 1000 files; add a larger one a folder at a time.

## Collections and tags

- **Collections** are separate indexes. Search one at a time. Names are lowercase letters, digits, `-` and `_`.
- The `documents` collection always exists. It can be cleared but not deleted.
- **Tags** are free-form labels (comma separated). You can filter search results by tag.
- **Re-embed** on a collection's page chunks and embeds all its documents again, from what Lantrn already stored. Use it after switching to an embedding model with a different vector size, which the existing vectors can't be mixed with. Nothing needs uploading again, and images aren't sent to the vision model again.

## Chunking

The ingest and crawl pages let you tune how documents are split under **Advanced settings**. The defaults work for most documents. Smaller chunks give more precise hits; larger chunks keep more context together.
