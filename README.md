# SharpSense

SharpSense is a local, headless semantic code analyzer and knowledge graph CLI for large .NET monorepos. It indexes
Roslyn-derived symbols and dependencies into a repository-scoped SQLite database, then exposes hybrid search, impact
analysis, and MCP tooling on top of that graph.

## Prerequisites

- .NET 10 SDK
- Bun (required when building the embedded UI assets bundled into `SharpSense.Cli`)
