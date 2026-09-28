# Memories

Memories attach Markdown notes to indexed code or document nodes in one workspace.

```bash
sharpsense memory add --workspace product --node-id 42 \
  --content "Keep this operation idempotent." --intent Invariant --tag reliability
sharpsense memory list --workspace product --node-id 42
sharpsense memory list --workspace product --node-id 42 --intent-filter Invariant
sharpsense memory get --workspace product --memory-id <memory-guid>
sharpsense memory get --workspace product --memory-ids <first-guid>,<second-guid>
sharpsense memory remove --workspace product --memory-id <memory-guid>
```

Replace placeholder IDs with values returned by search or memory commands. Quote values containing shell-special characters.

| Option | Purpose |
| --- | --- |
| `--node-id` | Required for add and list; an integer ID in the selected workspace. |
| `--content` | Required Markdown content for add. |
| `--memory-id` | A memory GUID for get or remove. |
| `--memory-ids` | Comma-separated GUIDs for batch get. |
| `--tag` | Repeatable tags for add; normalized and deduplicated. |
| `--intent` | Convention, Invariant, Todo, Warning, or Decision; defaults to Convention. |
| `--intent-filter` | Repeatable intent filters for list. |

Memories are immutable. Delete and attach a replacement when content changes. A memory is marked stale when its saved target hash differs from the current node hash.

Persistence binds memories to `CodeNodes.Id`, not a globally unique symbol name. Reindexing preserves memories while the owning canonical node survives; removing that node cascades deletion of its memories. Changing workspace sources can therefore remove attached notes after the next index.

[Context](context-command.md) and [trace](trace-command.md) include memory metadata only when requested. Retrieve full text with get. The [MCP tools](mcp-command.md) and UI share the same memory handlers and workspace isolation.
