# Browser UI

`sharpsense ui` starts the global workspace UI at `http://localhost:50069`. It can start with no registered workspaces and also lists existing registrations.

```bash
sharpsense ui
sharpsense ui --workspace product
sharpsense ui --url http://localhost:50100
```

The optional workspace selects the initial view; the server remains a global catalog. The listen URL must be a loopback HTTP(S) address.

## Workspace workflow

Use Workspaces to create or edit a source selection, discover available sources, or merge existing definitions. Open a workspace to run analysis, start/stop watch, inspect index diagnostics, browse the graph, search, try tools, and manage memories.

The explorer loads persisted directories and documents on demand. Graph nodes and optional relationships load progressively without a total-result cap. Progress counts and Pause/Resume controls stay available while loading. Projects, classes, interfaces, components, documents, and external dependencies are visible by default; the type menu can also show methods, properties, and fields. Choose a smaller folder or use Search to focus on a symbol. The UI uses Material UI, TanStack Router and React Query, and a Kiota-generated API client. Query state and clients are isolated by workspace.

An active indexing/watch job blocks source edits for that workspace. Stop the job, edit, and analyze again. Jobs belong to the running UI server; stopping the server cancels its jobs.

## HTTP contract

| Route | Scope |
| --- | --- |
| `GET/POST /api/workspaces` | Global catalog listing and creation. |
| `GET/PUT /api/workspaces/{workspaceId}` | Definition selected by route ID. |
| `POST /api/workspaces/discover` | Candidate sources for a repository root. |
| `POST /api/workspaces/merge` | Create an independent merged definition. |
| `GET/POST/DELETE /api/workspaces/{workspaceId}/indexing` | Read status, start analysis/watch, or stop a job. |
| `GET /api/tools` | Global tool catalog. |
| `GET /api/graph/nodes/page`, `GET /api/graph/edges/page` | Cursor pages for selected directory IDs, tied to one graph revision. |
| `GET /api/graph/nodes/{nodeId}/connections` | Selected node summary and paged distinct connected peers, independent of canvas scope and edge visibility. |
| Graph, tree, overview, tool execution, statistics, and memory routes | Require `X-SharpSense-Workspace: <workspace-guid>`. |

The required workspace header is declared in OpenAPI and generated into the client contract. Each request or job binds a fixed workspace scope; there is no mutable server-wide current workspace. The server validates Host and Origin against its loopback binding.

See the [UI development guide](../../../src/SharpSense.UI/README.md) for pnpm commands and API client generation, and [workspace explorer architecture](../architecture/workspace-tree.md) for graph loading.
