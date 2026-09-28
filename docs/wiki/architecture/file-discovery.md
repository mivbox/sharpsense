# Source discovery

A named workspace's `Sources` collection is the complete user-selected input plan. Paths are stored relative to its canonical repository root; the location of `workspace.yaml` and the process's later working directory do not change their meaning.

| Source kind | Input | Discovery boundary |
| --- | --- | --- |
| C# | `.csproj`, `.sln`, or `.slnx` | Roslyn/MSBuild project membership and semantic dependencies. |
| TypeScript | A tsconfig file or directory | `.ts` and `.tsx` files, supported config membership, and repository-local imports. |
| Markdown | File, directory, or glob | Explicit selected documentation paths and supported Markdown extensions. |

C# project references can be loaded to resolve symbols while the selected projects determine emitted scope. TypeScript imports and project/config references may include shared repository files outside the initially selected folder. These are language dependency rules, not automatic selection of every neighboring project.

Markdown selections are combined into one pass, which allows links between selected documents to resolve consistently. No default documentation glob is silently added to a named workspace. During watch, safe documentation-only file changes can reuse committed C# and TypeScript contributions while rerunning this complete Markdown pass; [watch reconciliation](incremental-watch.md) describes the input-sensitive reuse rules.

`WorkspaceCatalog` validates source shape and repository containment. Extraction also handles missing or invalid required inputs as failures, preserving the last committed graph. `WorkspaceGraphMerger` combines overlapping results and rejects incompatible duplicate identities rather than choosing an arbitrary definition.

## Filesystem filtering

`IWorkspaceFileDiscoverer` provides glob-based discovery with normalized repository-relative paths and the root `.gitignore` rules. Language adapters apply their own membership and generated/dependency-directory exclusions. C# membership follows MSBuild rather than treating a filesystem glob as a project.

TypeScript excludes dependency/build directories such as `node_modules`, `dist`, and `coverage`. Its supported source extensions are currently `.ts` and `.tsx`; JavaScript and `.mts`/`.cts` are not part of this source-discovery contract.

See [workspace setup](../cli/workspace-command.md), [TypeScript extraction](../extractors/typescript.md), and [watch reconciliation](incremental-watch.md).
