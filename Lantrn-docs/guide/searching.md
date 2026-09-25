# Searching

Type a question or a few words on the home page. Click a result to preview the passage in context, or open the full document.

## How it works

Every search runs two ways at once:

- **Meaning** — your query is embedded and compared to every chunk. Finds matches even when the words differ.
- **Keywords** — exact terms, like names, amounts and product codes, which embeddings tend to blur.

The two rankings are merged, so a result that is strong on only one side can still show up. If the query has no usable keywords (e.g. only punctuation), it searches by meaning only.

## Filters

| Filter     | Options                                  |
| ---------- | ---------------------------------------- |
| Collection | Which collection to search               |
| Type       | All, Documents, or Images (OCR)          |
| Results    | 5, 10, 20 or 50                          |
| Tags       | Only results with any of the chosen tags |

## Search assistant

If the assistant is turned on in [Settings](./configuration#search-assistant), a chat model reads the top results and writes an answer. Each claim cites the result it came from by number, so you can check it. You can ask follow-up questions about the same sources.

::: warning
The assistant sends document text to the chat endpoint. Use a local model if your documents must not leave your network.
:::
