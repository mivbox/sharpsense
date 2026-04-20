# Agent Directives & Workflow

* **Implementation & Testing:** Implement the feature. Strictly adhere to the Testing Requirements and the Reference
  Pattern provided. No redundant comments.
* **Self-Correction Loop:** Run local build/test autonomously. Debug any errors by reading the logs until achieving a
  100% pass rate.
* **Peer Review:** After a green build, explicitly invoke a secondary review step (e.g. Claude Sonnet 4.5 for
  syntax/logic and Claude Opus 4.6 for architecture) for verification before finalising.

# Project Context

* The standard logging directory for this application is:
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".SharpSense", "logs");`

# General

* Make only high-confidence suggestions when reviewing code changes.
* Always use the latest version C#, currently C# 14 features.
* **Never** change `Directory.Packages.props`, `Directory.Build.props`, `global.json`, or `NuGet.Config` unless
  explicitly asked.

# Naming Conventions

* **Avoid DTO Suffixes**: Do not append "DTO" to data transfer objects. Rely on namespaces to keep class names clean and
  domain-focused.
* **Namespace Conflict Resolution**: If mapping between layers and class names conflict (e.g. mapping a Domain ObjectA
  to an Application ObjectA), use the fully qualified domain name (FQDN) for the layer you are not currently operating
  in (e.g. Namespace.ObjectA) to resolve the ambiguity.

# Formatting

* Apply code-formatting style defined in `.editorconfig` at repo root.
* Prefer file-scoped namespace declarations and single-line using directives.
* Insert a newline before the opening curly brace of any code block (e.g. after if, for, while, foreach, using, try,
  etc.).
* Ensure that the final return statement of a method is on its own line.
* Use pattern matching and switch expressions wherever possible.
* Use `nameof` instead of string literals when referring to member names.

# Nullable Reference Types

* Declare variables non-nullable, and check for null at entry points.
* Always use `is null` or `is not null` instead of `== null` or `!= null`.
* Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

# Error Handling with FluentResults

* **Always use FluentResults** (`Result<T>`) for business logic errors; **never throw exceptions** for expected failure
  cases.

# Testing

* We use **xUnit SDK v3** for tests.
* Do not emit "Act", "Arrange" or "Assert" comments.
* Use **Moq** for mocking in tests.
* Use **AwesomeAssertions** (`.Should()` syntax) for assertions in unit test projects.
* Copy the existing style in nearby files for test method names and capitalisation.
* Test projects are located in `tests/` subdirectories within each service.

# Dependency Injection Standards

Never register services directly in `Program.cs`. You must strictly use modular `IServiceCollection` extension methods
based on architectural layers and vertical slices:

1. **Shared/Persistence:** Use `PersistenceServiceCollectionExtensions.AddPersistence()`.
2. **Application Features:** Use `{FeatureName}ServiceCollectionExtensions.Add{FeatureName}()` (e.g., `AddIndexing()`
   for CQRS handlers).
3. **Infrastructure Features:** Use
   `{FeatureName}InfrastructureServiceCollectionExtensions.Add{FeatureName}Infrastructure()` (e.g.,
   `AddIndexingInfrastructure()` for external/IO services).
   Chain these methods cleanly in the host setup.
