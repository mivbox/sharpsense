# Workspace explorer and graph loading

The explorer represents persisted indexed content, not a fresh recursive scan of the checkout. A workspace with no completed index can therefore have an empty tree even when its repository contains files.

`WorkspaceTreeRepository` projects immediate children from `Directories` and `Documents`, using `ProjectNodes` to identify project documents. `DirectoryClosures` supports descendant scoping elsewhere in graph queries. `WorkspaceTreeNode` is a response model; there is no separate persisted tree table.

## Requests

- `GET /api/tree?path=/` loads the root's immediate children.
- Expanding a directory requests that directory's children.
- `GET /api/graph/nodes/page?directoryIds=...&includeTotal=true` starts progressive node loading. Each response includes items, a graph revision, and a cursor for the next page.
- `GET /api/graph/edges/page` loads optional relationships using the same revision. The first page can include a total count; subsequent pages omit that work.
- `GET /api/graph/nodes/{nodeId}/connections` independently loads the inspector's selected node and distinct connected peers. The UI requests 20 peers at a time and exposes further pages with **Load more connections**. This query does not depend on the canvas scope or whether canvas relationships are enabled.
- Page sizes are bounded, but the total graph is not. React Query follows cursors automatically, retaining every loaded page while the scope is active. Pause/Resume controls stop or continue network work, and changing scope cancels obsolete requests.
- Numeric database IDs replace repeated canonical strings in the page transport. Node labels, paths, types, and relationship metadata remain available.

The graph revision changes in the same transaction as graph updates. Cursors bind the revision, workspace, and directory scope. A stale revision returns HTTP 409 so the UI can restart loading instead of combining different snapshots.

Inspector cursors bind the workspace, selected node ID, and revision. Each item contains a peer summary and every relationship to that peer, including incoming, outgoing, and self relationships. The response also includes the selected node summary, allowing an unloaded peer to be inspected without broadening canvas scope. Missing selected nodes return HTTP 404. Switching nodes or workspaces cancels pending requests, and a revision conflict retains the partial report until a fresh retry.

The legacy `/api/graph/view`, `/api/graph/nodes`, and `/api/graph/edges` routes are retired and return HTTP 404. They are absent from OpenAPI and the generated client; paged reads are the graph API contract.

The graph projection reads only fields needed for display, omitting embeddings and indexing text. Type filters control which loaded nodes are drawn. The initial selection shows projects, classes, interfaces, components, documents, and external dependencies, with an All types option for methods, properties, and fields as well. The renderer uses batched GPU buffers and runs layout in a web worker rather than creating a separate expensive scene object for each node and edge.

The browser indexes new pages incrementally instead of flattening and filtering every loaded page on each response. Relationships whose endpoints have not arrived wait until both endpoints are available. Filter changes or replacement pages rebuild the projection; changing scope creates an independent projection. Renderer buffers append new content and grow capacity as needed. These retained indexes trade memory for less repeated CPU work without limiting graph size. Worker layout requests keep one snapshot in flight and one replaceable pending snapshot, avoiding an unbounded queue during rapid paging.

Workspace-specific routes require `X-SharpSense-Workspace: <workspace-guid>`. The directory IDs and node IDs returned by these requests belong only to that workspace.

The UI maintains explorer and query state within the selected workspace. Successful indexing invalidates relevant data so the tree and graph reflect the newly committed snapshot. See [UI usage](../cli/ui-command.md) and [SQLite schema](../persistence/sqlite-schema.md).
