# Searching

## Filters

Type `tag:` or `in:` in the search box to filter by tag or collection. Quote names with spaces, e.g. `in:"Team docs"`. With several tags, documents with any of them match.

## How ranking works

Each search runs two ways and merges the results:

- **Meaning**: the query is embedded and compared to every chunk, so it matches even when the words differ.
- **Keywords**: catches names, amounts and codes that embeddings blur. Word forms match too ("invoices" finds "invoice"), using the keyword language from Settings.

With a [reranker](./configuration#reranker) on, the top candidates are then reordered. Neighbouring passages from the same document are shown as one result.

## Search assistant

When the [assistant](./configuration#search-assistant) is on, a chat model answers from the top results and cites them by number. You can ask follow-up questions about the same sources.
