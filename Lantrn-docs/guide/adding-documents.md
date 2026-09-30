# Adding documents

Documents are split into chunks, embedded and stored in a **collection**. You can add to your own collections and to any public one.

Uploads and crawls run in a background queue. You can close the page while it runs, and unfinished work resumes after a restart.

## Supported files

| Type     | Extensions                                                |
| -------- | --------------------------------------------------------- |
| PDF      | `.pdf`                                                    |
| Office   | `.docx`, `.pptx`, `.xlsx`, `.xls`, `.odt`, `.ods`, `.rtf` |
| E-mail   | `.eml`, `.msg`                                            |
| E-books  | `.epub`                                                   |
| Markdown | `.md`, `.markdown`                                        |
| Text     | `.txt`                                                    |
| HTML     | `.html`, `.htm`                                           |
| Data     | `.csv`, `.tsv`, `.json`, `.xml`, `.yaml`, `.yml`          |
| Images   | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`                  |

Images need a [vision model](./configuration#vision).

## Uploads

- Files can be up to 100 MB.
- ZIP archives are unpacked into one document per file, tagged with the folders they were in. Up to 1000 files and 500 MB unpacked.
- You can paste a screenshot anywhere on the Ingest page.
- A file with the same name in the same collection replaces the old one.

## Crawling

The crawler follows the sitemap and links but stays under one path, such as `/docs`. It respects `robots.txt` and won't crawl private or local network addresses.

A re-crawl only re-embeds pages that changed and removes pages that are gone. If it finds less than half the pages it found last time, nothing is removed, so a site that's briefly down doesn't empty the collection.

A public GitHub repository link (`https://github.com/owner/repo`, or `.../tree/main/src` for one folder) adds its code and docs instead, one document per file. Dependencies, build output and files over 512 KB are skipped. A repository can have at most 1000 files, so add large ones a folder at a time.

## Re-embed

**Re-embed** on a collection's page rebuilds its vectors from the stored text. Use it after changing the embedding model. Nothing needs to be uploaded again.
