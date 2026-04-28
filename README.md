# SharpSense

SharpSense is a local, headless semantic code analyzer and knowledge graph CLI for large .NET monorepos. It indexes
Roslyn-derived symbols and dependencies into a repository-scoped SQLite database, then exposes hybrid search, impact
analysis, and MCP tooling on top of that graph.

## Prerequisites

- .NET 10 SDK
- Bun (required when building the embedded UI assets bundled into `SharpSense.Cli`)

## CLI

### Package and share as a .NET tool

```bash
dotnet pack src/SharpSense.Cli/SharpSense.Cli.csproj -c Release -o artifacts/nuget
```

This creates a NuGet package such as `artifacts/nuget/SharpSense.Cli.1.0.0.nupkg` that can be copied to a shared
folder or published to a feed.

Install it from the folder that contains the `.nupkg`:

```bash
dotnet tool install --global --add-source /path/to/artifacts/nuget SharpSense.Cli
sharp-sense --help
```

Update to a newer shared package from the same source:

```bash
dotnet tool update --global --add-source /path/to/artifacts/nuget SharpSense.Cli
```

### Index a solution once

```bash
dotnet run --project src/SharpSense.Cli -- analyze SharpSense.sln
```

### Watch a solution for incremental updates

```bash
dotnet run --project src/SharpSense.Cli -- analyze SharpSense.sln --watch
```

### Skip embeddings during indexing

```bash
dotnet run --project src/SharpSense.Cli -- analyze SharpSense.sln --no-embeddings
```

### Use an explicit repository root

```bash
dotnet run --project src/SharpSense.Cli -- analyze src/SharpSense/SharpSense.sln --repo-root /path/to/repo
```

## Watch mode behaviour

- `analyze --watch` performs an initial full index, then applies incremental updates for changed files.
- Incremental updates currently track C# and Markdown file changes only.
- Supported file actions are add, modify, delete, and rename.
- `sharpsense.yaml`, `.sln`, and `.csproj` changes do not trigger incremental reconfiguration; rerun a full index after changing those files.
- Watch mode refreshes changed nodes and the outgoing dependency edges owned by the changed files. After broader API or symbol refactors, a full reindex is still the safest way to refresh the entire graph.
