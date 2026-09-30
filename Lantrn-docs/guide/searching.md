# Searching

Type a question or a few words on the home page. Click a result to preview the passage in context, or open the full document.

## Filtering from the search box

Type `tag:` or `in:` to filter by tag or collection; suggestions show as you type. Quote names with spaces, e.g. `in:"Team docs"`. When you search, the filters move into the controls under the box. With several tags, documents with any of them match.

## How it works

Every search runs two ways at once:

- **Meaning** — your query is embedded and compared to every chunk. Finds matches even when the words differ.
- **Keywords** — terms like names, amounts and product codes, which embeddings tend to blur. Word forms match too ("invoices" finds "invoice"), in the keyword language set in [Settings](./configuration#embeddings).

The two rankings are merged, so a result that is strong on only one side can still show up. If the query has no usable keywords (e.g. only punctuation), it searches by meaning only. With a [reranker](./configuration#reranker) on, the top candidates are then reordered by how well they answer the query.

Passages next to each other in the same document show up as one result, since the preview shows the neighbours anyway.

## Search assistant

If the assistant is turned on in [Settings](./configuration#search-assistant), a chat model reads the top results and writes an answer. Each claim cites the result it came from by number, so you can check it. You can ask follow-up questions about the same sources.

::: warning
The assistant sends document text to the chat endpoint. Use a local model if your documents must not leave your network.
:::
