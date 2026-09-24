# SharpSense workspace UI

Use Node.js 22.13+ and pnpm 11.8.0. The repository includes `.nvmrc` for Node version selection and pins pnpm in `package.json`.

The UI uses native Material UI components and theme tokens, a Kiota-generated TypeScript API client, and TanStack React
Query and TanStack Router. Features are organized as vertical slices: workspaces, graph exploration, search, tools,
index diagnostics, and memories. The app layer owns the shell and providers; shared code owns the generated transport,
view models, and reusable states.

Select **Entire workspace** in Explorer to cover files at the repository root and all subfolders. Nodes load
progressively, with optional relationships, progress counts, and **Pause** / **Resume** controls. There is no total node
or relationship cap. Projects, classes, interfaces, components, documents, and external dependencies are visible by default; use the type menu, including **All types**,
to choose what is drawn. Select individual folders or use Search to focus on a smaller part of the codebase. Scope,
type filters, and symbol selection are preserved in the URL.

Explorer uses React Query infinite queries over Kiota's `/api/graph/nodes/page` and `/api/graph/edges/page` clients.
Pages use compact numeric node references, stable cursors, and an index revision so results from different index
versions cannot be mixed. Obsolete requests are cancelled when switching scopes or workspaces. If the graph changes
during loading, retry to start from the new revision. Graph queries omit embeddings and indexing text. The renderer
batches nodes and edges into GPU buffers and performs layout in a web worker, keeping interaction on the UI thread.
New pages update persistent node indexes and visible relationships incrementally. Renderer buffers grow by capacity
and append only newly visible content; filters, scope changes, and replacement query pages rebuild the relevant
projection. Pending relationships wait for their endpoints, so loading order does not discard edges. These indexes
use additional memory to reduce repeated scans; the full graph remains available without truncation.

The symbol inspector loads its own connections through `/api/graph/nodes/{nodeId}/connections`, even when canvas
relationships are switched off. It shows 20 distinct connected nodes per page; **Load more connections** reveals
additional peers without a total limit. Each peer includes all incoming and outgoing relationship types. Selecting a
peer also works when that node has not loaded into the current canvas scope, and the selection survives a URL reload.
Changing the selected node or workspace cancels pending inspector requests. Revision conflicts preserve the already
loaded connections and offer a fresh retry. Legacy `/api/graph/view`, `/api/graph/nodes`, and `/api/graph/edges` routes
have been removed; clients must use the paged routes.

Open **Index status** from the sidebar or app bar to inspect repository coverage, embedding counts, the last successful
index, phase timings, and actionable diagnostics. The **Graph statistics** tool provides the same information without
selecting a symbol. Index timestamps describe completed work; they do not establish whether source files have changed
since that run. Indexes without recorded history show it as unknown until their next analysis.

## Run locally

Build and start the API from the repository root:

```bash
dotnet run --project src/SharpSense.Cli -- ui
```

The UI lists all registered workspaces and also works when none exist. To create one, enter a name and repository
directory, discover or add C# projects, TypeScript configurations, and Markdown globs, then analyze it or start watching.
The dashboard supports editing source selections and merging workspaces from the same repository. Stop its active
indexing/watch job before editing sources.

Definitions and independent databases live under `~/.sharpsense/workspaces/<id>/`; `SHARPSENSE_HOME` overrides this
location. SharpSense never reads or creates repository-local YAML configuration. Workspace selection is preserved in
the URL. Each selected workspace has an immutable Kiota client and its own React Query cache; requests keep their
original workspace identity when switching views.

The bundled UI is available at `http://localhost:50069`. For frontend development, open another terminal:

```bash
cd src/SharpSense.UI
pnpm install --frozen-lockfile
pnpm run dev
```

Vite proxies API requests to the local UI server. Set `SHARPSENSE_API_URL` when starting Vite to use another backend.
The API accepts same-origin requests on its loopback address. Use the Vite proxy for frontend development.

## Generate the API client

The pinned Kiota tool is declared in `.config/dotnet-tools.json`. From the repository root, generate against the
checked-in OpenAPI description:

```bash
./scripts/generate-client.sh
```

To refresh that description from a running UI API and regenerate:

```bash
./scripts/generate-client.sh --refresh
# Another server:
SHARPSENSE_API_URL=http://localhost:50070 ./scripts/generate-client.sh --refresh
```

Refreshing uses `curl` and `jq`. Generated files live in `src/shared/api/generated`; change the backend endpoint contracts
and regenerate instead of editing those files. Feature hooks use React Query over workspace-bound Kiota clients. The OpenAPI contract declares the required
`X-SharpSense-Workspace` UUID header on graph, query, diagnostics, and memory operations. Kiota accepts the header
through its standard `requestConfiguration.headers` bag. Catalog and indexing-control operations are global or use
the workspace ID in their route.

## Validate

Run these commands from the repository root:

```bash
pnpm --dir src/SharpSense.UI run typecheck
pnpm --dir src/SharpSense.UI run lint
pnpm --dir src/SharpSense.UI run format:check
pnpm --dir src/SharpSense.UI run test:unit
pnpm --dir src/SharpSense.UI run build
dotnet build src/SharpSense.Cli -c Release -p:GeneratePackageOnBuild=false
pnpm --dir src/SharpSense.UI exec puppeteer browsers install chrome
pnpm --dir src/SharpSense.UI run test:e2e
```

The browser tests use isolated temporary repositories and home directories. They cover workspace creation, switching,
merging, watching, graph exploration, independent inspector pagination and cancellation, search, all five typed tools,
and memories, and fail on uncaught browser errors. Run `pnpm --dir src/SharpSense.UI run test:e2e:inspector` for the
inspector regression alone after building the UI. CI and release
validation run the .NET suite, UI checks, unit tests, and browser workflows before publishing.

`pnpm --dir src/SharpSense.UI run test:e2e:indexing` exercises a disposable C#/TypeScript/Markdown workspace through
the real CLI and watch API. It verifies actual source-contribution reuse for an existing documentation edit, graph
identity preservation through documentation creation/rename/deletion, and updated relationships after code and mixed
edits. Its verbose logs and snapshot summary are retained in the reported artifact directory; fixture repositories
and workspace homes are removed afterward.

To profile an existing large graph, start the UI server, find the workspace UUID with `sharpsense workspace list`, and run:

```bash
SHARPSENSE_PROFILE_URL=http://localhost:50069 \
SHARPSENSE_PROFILE_WORKSPACE="your-workspace-uuid" \
pnpm --dir src/SharpSense.UI exec tsx e2e/graph-paging.profile.ts
```

The profile uses GET requests and expects more than 1,000 nodes and 2,000 relationships. It records first graph data,
full loading time, transferred bytes, type-filter response, and browser frame intervals during an orbit drag.
Screenshots and `profile.json` are saved in the reported temporary directory; set `SHARPSENSE_PROFILE_ARTIFACTS` to choose another location.

To compare CPU preparation independently of network transfer, WebGL drawing, and worker layout, run the deterministic
212,849-node / 682,838-edge fixture from the UI directory:

```bash
pnpm exec node --expose-gc --import tsx tests/graphPreparation.profile.ts
pnpm exec node --expose-gc --import tsx tests/graphPreparation.profile.ts baseline
# Append "all" to either command to include every node type.
```

The profile reports preparation time, backing-buffer allocations, and retained heap. Keep browser or other heavy
profiling work idle during measurement; results depend on the machine and exclude GPU and worker copies.

Use MUI's components, palette, typography, spacing, and responsive layout props. Keep custom CSS stylesheets out of the UI.
Run `pnpm --dir src/SharpSense.UI run format` from the repository root after editing handwritten files. ESLint and Prettier check the source and TypeScript tooling;
generated Kiota files and the OpenAPI snapshot are excluded from formatting and linting.
