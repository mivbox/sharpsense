---
title: ".NET Build and Code Conventions"
type: architecture
tags: [csharp, build, testing]
created: 2026-09-27
updated: 2026-09-27
confidence: high
---

# .NET build and code conventions

SharpSense targets .NET 10. `global.json` selects a stable .NET 10 SDK, allowing installed feature-band updates. `Directory.Build.props` owns the target framework version, nullable references, implicit usings, language version, SDK analyzers, NuGet audit level, and warnings policy. XML documentation output enables the SDK unused-import check; missing XML comments are exempt, while invalid contract references are reported. `Directory.Common.props` owns test-project defaults and explicit test-only `InternalsVisibleTo` declarations. The SDK generates these assembly attributes once.

Projects use `$(NetCoreAppVersion)` and keep only their own build requirements. Copied integration fixtures explicitly target `net10.0` and retain any explicit package versions because they must build outside this checkout. Package versions remain centralized in `Directory.Packages.props`; the Mapperly integration fixture is the intentional exception.

The shared props follow the generic Policy AI build conventions without importing service-specific packages or production friend assemblies. When SharpSense moves into that repository, reconcile these files with its inherited props instead of maintaining competing copies. The TypeScript project remains independent.

## Formatting

- Use four spaces in C# and two in project/props XML. Put usings outside file-scoped namespaces, without a separate System group.
- Use Allman braces, brace control flow, and put statements on separate lines. Separate setup, work, assertions, and the final return with meaningful blank lines.
- When arguments wrap, put each argument on its own line. Put object initializer assignments on separate lines.
- Put successive LINQ, EF, DI, and Moq fluent calls on new lines. Keep simple assertions such as `result.Should().BeTrue()` together; wrap longer assertions.
- Use `_camelCase` for private fields and PascalCase for types and members. Prefer `var`, pattern matching, `nameof`, and nullable annotations to redundant defensive code.
- Custom asynchronous methods omit the `Async` suffix. Framework overrides and interface implementations retain the required framework name. Avoid `DTO` and generic `Service` suffixes.
- Document public contracts and preserve comments that explain a decision or invariant. Use `FluentResults` with typed `ServiceError` values for expected application failures.

`.editorconfig` and SDK analyzers enforce supported rules. Fluent layout is also a review convention; no additional analyzer package is required. Historical EF migration namespace formatting and one existing migration import have narrow exemptions so migration history is not rewritten.

## Visibility and tests

Public types are cross-assembly contracts, transport models, options, shared value/diagnostic APIs, or feature registration entry points. Application handlers, extractors, repositories, EF contexts/configuration, and workspace storage implementations are internal. Production assemblies do not use friend access; selected test assemblies and Moq can access internals through `InternalsVisibleTo`.

Use xUnit v3, Moq, and AwesomeAssertions. Name tests for their observable trigger and result with `When..._Then...`. Separate test phases with blank lines instead of Arrange/Act/Assert comments. Test behavior through supported public/internal boundaries, never reflection or private members. Preserve exact exception checks with `ThrowExactly`/`ThrowExactlyAsync` when required. Pass the xUnit cancellation token to asynchronous operations. Do not retain tests that only assert interface assignability, assembly names, or static repository file layouts. Keep plugin metadata checks out of the .NET integration suite; exercise the application through its supported entry points.

Application tests mirror feature folders. CLI unit tests cover parsing, command policy, and presentation in isolation, including the shared analysis snapshot reducer. Infrastructure tests cover extraction and SQLite behavior; integration tests cover real hosts, HTTP, processes, and watch orchestration. SQLite fixtures use typed constructors and preserve migration/vector-extension coverage. Tests that mutate process state run without parallel peers.

## Validation

```bash
dotnet restore SharpSense.sln
dotnet format SharpSense.sln --no-restore --severity warn --verify-no-changes --exclude src/SharpSense.Infrastructure/Persistence/Migrations
dotnet build SharpSense.sln --no-restore -c Release -t:Rebuild -p:GeneratePackageOnBuild=false
dotnet test SharpSense.sln --no-build -c Release
```

For subsequent backend-only builds, `-p:FrontendBuildCompleted=true` reuses an already built `src/SharpSense.UI/dist`; build the UI first on a clean checkout. Tool packaging additionally requires a local pack/install smoke check, including workspace creation and analysis. No validation command publishes a package.

For EF design-time checks, explicitly select the graph context (the assembly also contains a transient execution-log context):

```bash
dotnet ef dbcontext info --project src/SharpSense.Infrastructure --startup-project src/SharpSense.Infrastructure.MigrationsHost --context SharpSenseDbContext
```
