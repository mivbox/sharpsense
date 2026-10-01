# SharpSense development conventions

SharpSense targets .NET 10. Use the SDK and language version selected by `global.json` and the shared build props.
Keep changes within the requested scope and follow the nearest maintained feature and test examples.

## Code layout and readability

- Follow `.editorconfig` and [the .NET conventions](docs/wiki/architecture/dotnet-conventions.md).
- Use file-scoped namespaces, four-space indentation, Allman braces and `_camelCase` private fields.
- Separate setup, work, assertions and the final return with meaningful blank lines. Put wrapped arguments and
  initializer members on separate lines. Put successive LINQ, EF, DI and Moq calls on separate lines; keep simple
  assertions readable on one line.
- Use purpose-specific names without generic `Service` or `DTO` suffixes. Custom asynchronous methods omit `Async`;
  framework overrides and interface implementations retain their required names.
- Prefer `var`, `nameof`, pattern matching and accurate nullable annotations. Validate external input and public
  boundaries; avoid redundant null checks inside code whose contract already guarantees a value.
- Document public contracts deliberately. Preserve implementation comments that explain a decision or invariant;
  omit comments that merely repeat the code.
- Remove obsolete code and unused dependencies when replacing an implementation. Add helpers and abstractions when
  they clarify responsibilities or provide a real boundary, without creating wrappers solely for uniformity.

## Visibility

- Implementation classes are normally `internal sealed`. Use internal abstract/static or unsealed classes when
  inheritance or the existing design requires them.
- Keep handlers, repositories, extractors, adapters, EF contexts/configuration, CLI commands and host coordinators
  internal. Resolve implementations through existing contracts and feature registration methods.
- Public types need a cross-assembly purpose: contracts, request/response models, shared options, shared value or
  diagnostic APIs, or registration entry points. Dependency injection and testing alone do not justify public types.
- Use the existing test-only `InternalsVisibleTo` declarations for supported internal boundaries. Production
  assemblies use contracts rather than friend access. Never use reflection to inspect or invoke members, and never
  test private members. Typed assembly markers used by framework composition are permitted.

## CQRS and vertical slices

- Keep each use case under `src/SharpSense.Application/{Feature}/{Operation}/`, with its command/query and
  operation-specific models in the operation's `Models/` directory. Tests mirror the feature layout.
- Implement `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>` with internal handlers. Keep
  command/query contracts public where they cross assembly boundaries.
- Handlers own application orchestration. CLI, MCP and HTTP adapters bind input, invoke the handler and format its
  result. Keep EF access and external-tool implementation details in Infrastructure.
- Keep one minimal HTTP endpoint per file, with a named handler and route-builder extension. Compose related
  endpoints in feature route-group extension files, and keep transport models in the feature's `Models/` directory.
- Put feature interfaces in `{Feature}/Abstractions/` and shared feature models in `{Feature}/Models/`. Do not add an
  Application `Features/` root or an `Infrastructure/` folder for its interfaces.
- Register handlers through `{Feature}ServiceCollectionExtensions.Add{Feature}()` and infrastructure through
  `{Feature}InfrastructureServiceCollectionExtensions.Add{Feature}Infrastructure()`. Compose these at the host.
  Keep `Program.cs` focused on startup and command/route composition.
- Reuse the existing direct handler contracts. Preserve transaction ownership in repositories and reuse shared
  behavior through deliberate contracts; do not introduce a mediator, generic unit of work or extra dispatch layer
  to reproduce another repository's architecture.
- Return FluentResults for expected application failures, using `ServiceError` when a typed failure is mapped by
  callers. Preserve unexpected exceptions and cancellation. Transport adapters own exit codes and HTTP/MCP mapping.

Read [vertical slices](docs/wiki/architecture/vertical-slice-application.md),
[commands and queries](docs/wiki/architecture/cqrs-pipeline.md), or
[host composition](docs/wiki/architecture/host-composition.md) when changing those boundaries.

## Tests

- Use the existing root `tests/SharpSense.*` projects with xUnit v3, Moq and AwesomeAssertions.
- Name tests `WhenCondition_ThenOutcome`, with one underscore separator. Separate phases with whitespace rather
  than Arrange/Act/Assert comments.
- Test observable behavior through public or supported internal boundaries. Do not add assertions solely for
  private details, interface assignability, assembly names or static repository layouts.
- Keep fixtures proportional to the behavior. Direct construction is fine; use shared setup or a factory when it
  removes meaningful duplication. Assert port interactions only when they are part of the behavior being proved.
- Reuse the existing SQLite, host and process fixtures for integration behavior. Pass the xUnit cancellation token
  to asynchronous operations, isolate temporary state and dispose hosts, processes, subscriptions and databases.

## Workspace and frontend boundaries

- Resolve runtime storage through `SharpSenseHome.Resolve`: an absolute `SHARPSENSE_HOME`, otherwise `~/.sharpsense`.
  Keep logs under `logs/` and workspaces under `workspaces/<workspace-id>`. Do not create project-local YAML config.
- Keep workspace identity fixed per CLI/MCP host, HTTP request and background job. CLI selection uses explicit
  `--workspace` or the saved `workspace use` default. Only MCP discovers a workspace from its launch directory
  when exactly one registered root contains it; MCP never reads the CLI default. `--workspace` overrides discovery.
- The TypeScript UI remains independent. Preserve pnpm, native Material UI, Kiota-generated API types and TanStack
  Query conventions when UI changes are requested. Do not hand-edit generated clients.

## Validation and documentation

- Use the commands in [the .NET conventions](docs/wiki/architecture/dotnet-conventions.md) for restore, formatting,
  Release build and tests. A backend-only build may reuse existing frontend assets with
  `-p:FrontendBuildCompleted=true`; a clean checkout needs its frontend built first.
- Run checks appropriate to the change, inspect the final diff and report what passed or remains unverified.
  Do not weaken assertions or analyzers to make a check pass. Preserve historical EF migration files during cleanup.
- Keep shared build/package changes within the user's requested scope. Dependency upgrades are separate from
  ordinary formatting or architecture cleanup.
- Update relevant wiki pages when behavior or architecture changes, follow their frontmatter conventions, and
  record architectural changes in `docs/wiki/log.md`.
