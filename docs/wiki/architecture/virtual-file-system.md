# Filesystem boundaries

Infrastructure uses `System.IO.Abstractions.IFileSystem` for testable configuration, path, discovery, and document operations. Application handlers depend on feature interfaces rather than opening files directly.

`WorkspaceCatalog` owns registered definitions under the resolved SharpSense home. `IRepositoryWorkspace` provides the selected root, database path, and relative-path operations. A selected `WorkspaceSelection` is registered explicitly; there is no production factory fallback that invents a workspace from a directory.

Roslyn loading sits behind its workspace abstractions, while source analysis can operate on an already loaded workspace. Markdown and TypeScript discovery/read boundaries allow focused tests without requiring every test to launch an external toolchain.

Some behavior requires the actual operating system: file-handle exclusivity, process execution, SQLite native integration, and MSBuild workspace loading. Tests for those boundaries use isolated temporary directories and an explicit `SHARPSENSE_HOME` rather than the developer's real catalog.

Use the abstraction at filesystem boundaries without pretending a mock proves native locking or process behavior. Configuration tests should exercise home-owned definitions and selected sources; production code never falls back to project-local `sharpsense.yaml`.

See [host composition](host-composition.md), [source discovery](file-discovery.md), and [C# extraction](../extractors/csharp.md).
