# C# extraction

C# sources use Roslyn and MSBuild. Select explicit `.csproj`, `.sln`, or `.slnx` inputs in a [workspace](../cli/workspace-command.md); there is no positional analyze target.

## Analysis

The workspace loader resolves project membership, references, parse options, and compilation context. Referenced projects are loaded from source for semantic resolution even when their compiled DLLs exist. Building a project does not change the source identities or cross-project relationships in the selected graph.

Node and edge extraction uses syntax and semantic symbols to identify declarations, calls, type relationships, and structural containment. XML `summary` and `remarks` contribute searchable documentation. Method-body hashes ignore trivia so formatting-only changes need not invalidate existing embeddings or memories.

Canonical C# identity includes the owning project. Two projects can declare the same fully qualified name and still produce distinct graph nodes. Partial declarations within one project are consolidated by symbol identity. Do not deduplicate unrelated projects solely by name.

## Errors and warnings

SharpSense consumes Roslyn's syntax and semantic models without requiring a successful compiler emit. Code containing compiler errors can still contribute declarations and resolvable relationships when its projects load; unresolved relationships may be absent.

Warning promotion is disabled only in SharpSense's design-time MSBuild workspace. MSBuild and NuGet warnings, including vulnerability advisories, remain available in index diagnostics. This does not edit project files, change normal build or CI policies, disable NuGet auditing, or repair vulnerable dependencies.

Roslyn reports both MSBuild warnings and errors as workspace failures. SharpSense recovers their original severity from private temporary MSBuild diagnostic logs and deletes those captures after loading. Only failures matched to recorded warning events are treated as warnings; unknown failures remain fatal.

Actual project-loading errors remain fatal. Errors already recorded in restore assets are not silently reclassified as warnings. Missing SDKs, failed package resolution, invalid projects, and unreadable required inputs can prevent loading; the previous committed graph is preserved if a required source fails.

## Watching and limitations

Suitable modified-file events can update documents in a warm Roslyn workspace. Project membership, structural changes, and generator/configuration inputs can require reloading. A named-workspace update still extracts and reconciles the complete selected graph, rather than persisting only the changed file.

Analysis depends on the repository's SDK, package restore, and MSBuild configuration. Dynamic dispatch, reflection, external implementation details, and runtime-only relationships are not a guarantee of complete static graph coverage. Review indexing diagnostics when loading or extraction fails.

See [source discovery](../architecture/file-discovery.md), [watch reconciliation](../architecture/incremental-watch.md), and [identity persistence](../persistence/sqlite-schema.md).
