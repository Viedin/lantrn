# AI assistants (MCP)

Lantrn runs an [MCP](https://modelcontextprotocol.io) server at `/mcp`, so assistants like Claude Code, VS Code and Cursor can search your documents while they work. It has three read-only tools: `list_collections`, `search` and `get_document`.

The assistant signs in with an admin's [API key](./api#api-keys), so it can search every collection.

## Claude Code

```sh
claude mcp add --transport http lantrn https://lantrn.example.com/mcp \
  --header "Authorization: Bearer $LANTRN_API_KEY"
```

## VS Code

In `.vscode/mcp.json`:

```json
{
  "servers": {
    "lantrn": {
      "type": "http",
      "url": "https://lantrn.example.com/mcp",
      "headers": { "Authorization": "Bearer ${input:lantrn-key}" }
    }
  },
  "inputs": [
    { "type": "promptString", "id": "lantrn-key", "description": "Lantrn API key", "password": true }
  ]
}
```

Other clients work the same way: point them at the `/mcp` URL over Streamable HTTP and send the key in the `Authorization` header.
