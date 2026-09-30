# AI assistants (MCP)

Lantrn runs an [MCP](https://modelcontextprotocol.io) server at `/mcp` with three read-only tools: `list_collections`, `search` and `get_document`. It uses Streamable HTTP and authenticates with an [API key](./api#api-keys) in the `Authorization` header.

For example, in Claude Code:

```sh
claude mcp add --transport http lantrn https://lantrn.example.com/mcp \
  --header "Authorization: Bearer $LANTRN_API_KEY"
```
