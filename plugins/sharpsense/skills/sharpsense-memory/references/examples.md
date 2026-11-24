# Memory audit workflows and CLI equivalents

## Auditing stale notes

1. Call `list_memories({staleOnly: true})` (or read `sharpsense://workspace/memories/stale`) to see all stale memory GUIDs, target node IDs, and previews.
2. Batch-fetch full contents with `get_memories({memoryIds: [...]})`.
3. Read each target symbol's current source and compare against the note:
   - **Still accurate**: leave in place, or (if the user asked to refresh stale notes) `attach_memory` with the updated hash and then `delete_memory` on the old GUID.
   - **Outdated**: report the discrepancy and replace or delete only when authorized.

## CLI equivalents

```bash
sharpsense memory list --workspace my-app --stale-only --toon
sharpsense memory list --workspace my-app --intent-filter Invariant --tag sqlite --query transaction --toon
sharpsense memory get --memory-ids <guid1>,<guid2> --workspace my-app --toon
sharpsense memory add --node-id 42 --intent Invariant --tag sqlite --content "Keep read transaction deferred." --workspace my-app
sharpsense memory remove --memory-id <old-guid> --workspace my-app
```
