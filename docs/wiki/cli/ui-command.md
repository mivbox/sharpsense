# Browser UI

`sharpsense ui` starts the global workspace UI at `http://localhost:50069`. It can start with no registered workspaces and also lists existing registrations.

```bash
sharpsense ui
sharpsense ui --workspace product
sharpsense ui --url http://localhost:50100
```

The optional workspace selects the initial view; the server remains a global catalog. The listen URL must be a loopback HTTP(S) address. It owns the listener; ambient Kestrel endpoints from appsettings or environment variables cannot override it.

## Workspace workflow

Use Workspaces to create or edit a source selection, discover available sources, or merge existing definitions. Open a workspace to run analysis, start/stop watch, inspect index diagnostics, browse the graph, search, try tools, and manage memories.

The explorer loads persisted directories and documents on demand. Graph nodes and optional relationships load progressively without a total-result cap. A deleted selected symbol clears its inspector actions; a server restart refreshes cached graph data even before its first index. Progress counts and Pause/Resume controls stay available while loading. Projects, classes, interfaces, components, documents, and external dependencies are visible by default; the type menu can also show methods, properties, and fields. Choose a smaller folder or use Search to focus on a symbol. The UI uses Material UI, TanStack Router and React Query, and a Kiota-generated API client. Query state and clients are isolated by workspace.

An active indexing/watch job blocks source edits for that workspace. Stop the job, edit, and analyze again. Jobs belong to the running UI server; stopping the server cancels its jobs.

## Live analysis progress

Analysis and watch updates show phase, per-language source activity, elapsed time, embedding progress, and committed graph counts. Warnings remain available alongside the latest operation summary. A completed phase does not imply a saved graph: the graph refreshes only after a successful commit.

The browser receives status snapshots over server-sent events (SSE). Each connection starts with current state, including after reconnecting; it does not replay an unbounded event history. Progress bursts are coalesced, and a slow or closed browser does not delay or cancel indexing. When the stream is unavailable, status polling provides a fallback.

The CLI and UI host consume the same application notification contract. SSE exposes jobs owned by that UI host; it does not monitor analysis launched in another CLI process. Switching workspace closes the old browser subscription while the server's job continues.

## HTTP contract

| Route | Scope |
| --- | --- |
| `GET/POST /api/workspaces` | Global catalog listing and creation. |
| `GET/PUT /api/workspaces/{workspaceId}` | Definition selected by route ID. |
| `POST /api/workspaces/discover` | Candidate sources for a repository root. |
| `POST /api/workspaces/merge` | Create an independent merged definition. |
| `GET/POST/DELETE /api/workspaces/{workspaceId}/indexing` | Read status, start analysis/watch, or stop a job. |
| `GET /api/workspaces/{workspaceId}/indexing/events` | SSE `status` snapshots for the route workspace; periodic `heartbeat` events keep idle connections active. |
| `GET /api/tools` | Global tool catalog. |
| `GET /api/graph/nodes/page`, `GET /api/graph/edges/page` | Cursor pages for selected directory IDs, tied to one graph revision. |
| `GET /api/graph/nodes/{nodeId}/connections` | Selected node summary and paged distinct connected peers, independent of canvas scope and edge visibility. |
| Graph, tree, overview, tool execution, statistics, and memory routes | Require `X-SharpSense-Workspace: <workspace-guid>`. |

The indexing stream and ordinary status response share an OpenAPI model. A host stream ID and monotonically increasing status sequence order progress updates independently from the committed graph revision. Generated Kiota models deserialize stream snapshots into the workspace's React Query cache.

The required workspace header is declared in OpenAPI and generated into the client contract. Each request or job binds a fixed workspace scope; there is no mutable server-wide current workspace. The server validates Host and Origin against its loopback binding. Trailing slashes retain the same workspace and database-initialization policy. Indexing failures expose ProblemDetails messages to the browser.

See the [UI development guide](../../../src/SharpSense.UI/README.md) for pnpm commands and API client generation, and [workspace explorer architecture](../architecture/workspace-tree.md) for graph loading.
