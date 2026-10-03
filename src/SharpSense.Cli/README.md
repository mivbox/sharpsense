# SharpSense CLI development

For installation, workspace setup, queries and agent integrations, see the [SharpSense README](../../README.md).
The NuGet package uses that same README.

## Install from source

Requires the .NET 10 SDK, Node.js 22.13+ and pnpm 12.6.0. Packaging builds and embeds the UI;
running the installed CLI and UI does not require Node.js or pnpm.

```bash
git clone https://github.com/mivbox/sharpsense.git
cd sharpsense
dotnet pack src/SharpSense.Cli/SharpSense.Cli.csproj -c Release -p:GeneratePackageOnBuild=false -o artifacts/nuget
dotnet tool install --global --add-source ./artifacts/nuget SharpSense.Cli
sharpsense --version
sharpsense --help
```

## Development

Follow [AGENTS.md](../../AGENTS.md) and the [.NET build and test conventions](../../docs/wiki/architecture/dotnet-conventions.md).
See the [UI guide](../SharpSense.UI/README.md) for frontend development and API client generation.

To index this checkout:

```bash
dotnet run --project src/SharpSense.Cli -- workspace create sharpsense --workspace-root "$PWD" \
  --csharp SharpSense.sln \
  --typescript src/SharpSense.UI/tsconfig.json \
  --markdown "docs/**/*.md"
dotnet run --project src/SharpSense.Cli -- analyze --workspace sharpsense
```
