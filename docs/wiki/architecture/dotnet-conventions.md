---
title: ".NET Build and Code Conventions"
type: architecture
tags: [csharp, build, testing]
created: 2026-09-27
updated: 2026-10-02
confidence: high
---

# .NET build and code conventions

SharpSense targets .NET 10. `global.json` selects a stable .NET 10 SDK, allowing installed feature-band updates. `Directory.Build.props` owns the target framework version, nullable references, implicit usings, language version, SDK analyzers, NuGet audit level, and warnings policy. XML documentation output enables the SDK unused-import check; missing XML comments are exempt, while invalid contract references are reported. `Directory.Common.props` owns test-project defaults and explicit test-only `InternalsVisibleTo` declarations. The SDK generates these assembly attributes once.

Projects use `$(NetCoreAppVersion)` and keep only their own build requirements. Copied integration fixtures explicitly target `net10.0` and retain any explicit package versions because they must build outside this checkout. Package versions remain centralized in `Directory.Packages.props`; the Mapperly integration fixture is the intentional exception.

MSBuild stays on the .NET 10-compatible 18.9.x line; ASP.NET Core 10 requires `Microsoft.OpenApi` 2.x. Upgrade those lines only with a compatible framework.

The shared props define this repository's backend build conventions. The TypeScript project keeps its own pnpm, TypeScript, lint, and formatting configuration.

## Formatting

- Use four spaces in C# and two in project/props XML. Put usings outside file-scoped namespaces, without a separate System group.
- Keep each top-level type in a file named after that type. Nested settings and private implementation records may stay with their owner.
- Keep simple auto-properties compact, such as `public string Name { get; init; }`. Use Allman braces for method and control-flow bodies, brace control flow, and put statements on separate lines. Separate setup, work, assertions, and the final return with meaningful blank lines.
- When arguments wrap, put each argument on its own line. Put object initializer assignments on separate lines.
- Put successive LINQ, EF, DI, and Moq fluent calls on new lines. Keep simple assertions such as `result.Should().BeTrue()` together; wrap longer assertions.
- Use `_camelCase` for private fields and PascalCase for types and members. Prefer `var`, pattern matching, `nameof`, and nullable annotations to redundant defensive code.
- Custom asynchronous methods omit the `Async` suffix. Framework overrides and interface implementations retain the required framework name. Avoid `DTO` and generic `Service` suffixes.
- Document public contracts and preserve comments that explain a decision or invariant. Use `FluentResults` with typed `ServiceError` values for expected application failures.

`.editorconfig` and SDK analyzers enforce supported rules. Fluent layout is also a review convention; no additional analyzer package is required. Historical EF migration namespace formatting and one existing migration import have narrow exemptions so migration history is not rewritten.

## Visibility and tests

Public types are cross-assembly contracts, transport models, options, shared value/diagnostic APIs, or feature registration entry points. Application handlers, extractors, repositories, EF contexts/configuration, and workspace storage implementations are internal. Production assemblies do not use friend access; selected test assemblies and Moq can access internals through `InternalsVisibleTo`.

Use xUnit Core Framework v3 (package version 4.0.1), Moq, and AwesomeAssertions. The `xunit.v3.mtp-off` package keeps `dotnet test` on VSTest with the Coverlet collector. Integration tests use `[assembly: Parallelization(Mode = ParallelMode.None)]` because they share process state.

Name tests for their observable trigger and result with `When..._Then...`. Separate test phases with blank lines instead of Arrange/Act/Assert comments. Test behavior through supported public/internal boundaries, never reflection or private members. Preserve exact exception checks with `ThrowExactly`/`ThrowExactlyAsync` when required. Pass `TestContext.Current.CancellationToken` through async helpers to cancellable operations. Link test-owned timeouts and host lifetimes to it. Cleanup uses independent, bounded waits so cancellation cannot leave resources running; framework disposal and APIs without token overloads retain their signatures. Do not retain tests that only assert interface assignability, assembly names, or static repository file layouts. Keep plugin metadata checks out of the .NET integration suite; exercise the application through its supported entry points.

Keep expectations visible after the action rather than burying them in complex mock predicates. Assert identities and values when a count or status alone could pass with incorrect results. Ranking tests assert order; filtering tests include an excluded candidate; deletion tests establish that the target existed. Ensure setup and indexing succeeded before comparing snapshots. Share fixtures when they remove meaningful setup duplication, and let the fixture own cancellation and disposal. Keep transport tests at the real CLI, HTTP or MCP boundary; remove duplicate tests of handlers that only forward a call when that behavior is already exercised there.

Application tests mirror feature folders. CLI unit tests cover parsing, command policy, and presentation in isolation, including the shared analysis snapshot reducer. Infrastructure tests cover extraction and SQLite behavior; integration tests cover real hosts, HTTP, processes, and watch orchestration. SQLite fixtures use typed constructors and preserve migration/vector-extension coverage. Tests that mutate process state run without parallel peers.

## Validation

[CI](../../../.github/workflows/ci.yaml) runs the .NET build and tests, formatting, TypeScript checks, frontend unit tests and browser workflows in one `build-and-test` job. It runs for pull requests and pushes to main; the job remains the required branch check.

```bash
dotnet restore SharpSense.sln
dotnet format SharpSense.sln --no-restore --severity warn --verify-no-changes --exclude src/SharpSense.Infrastructure/Persistence/Migrations
dotnet build SharpSense.sln --no-restore -c Release -t:Rebuild -p:GeneratePackageOnBuild=false
dotnet test SharpSense.sln --no-build -c Release
```

For subsequent backend-only builds, `-p:FrontendBuildCompleted=true` reuses an already built `src/SharpSense.UI/dist`; build the UI first on a clean checkout. Tool packaging additionally requires a local pack/install smoke check, including workspace creation and analysis.

No validation command publishes a package.

For EF design-time checks, explicitly select the graph context (the assembly also contains a transient execution-log context):

```bash
dotnet ef dbcontext info --project src/SharpSense.Infrastructure --startup-project src/SharpSense.Infrastructure.MigrationsHost --context SharpSenseDbContext
```

## Distribution assets

The tool packages the Tree-sitter core library and TypeScript/TSX grammars for its supported runtimes.
Other grammars and the ONNX GenAI native libraries are excluded during asset resolution. The BERT embedder
uses base ONNX Runtime; keep its native libraries and the connector's managed dependencies. Verify actual
embedding generation and both TypeScript parsers from an installed package after changing these filters.

The MIT project license is included in the tool package.

Inspect a fresh package staging directory when validating asset removal; an old publish directory may contain
files from earlier builds. The release version action uses its default conventional-commit patterns, including
scoped `feat(scope):` changes and breaking `!:` markers.
