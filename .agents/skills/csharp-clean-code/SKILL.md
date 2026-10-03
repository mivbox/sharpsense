---
name: csharp-clean-code
description: Apply C# coding standards when writing, refactoring or reviewing production code for clear responsibilities, readable control flow, deliberate visibility and consistent formatting. Use for ordinary implementation and cleanup; test design belongs to csharp-behavior-tests.
---

# C# coding standards

Read the applicable `AGENTS.md`, `.editorconfig` and the nearest maintained implementation. Apply these coding
standards within the repository's contracts and explicit task instructions. Keep its architecture, dependencies
and public API unless changing them is part of the task.

## Load the relevant examples

Read only the references needed for the code being changed:

- [Readable C#](references/readable-csharp.md) for method layout, naming, fluent calls, comments and helper decisions.
- [Features and endpoints](references/features-and-endpoints.md) when changing handlers, registration or HTTP endpoints.

The examples illustrate the standards with generic contracts. Adapt them to local APIs and explicit instructions;
they do not require another repository or additional packages.

## Make the responsibility clear

- Give each top-level type its own matching file. Keep private nested types with their owner when they clarify
  that owner's implementation. Split by responsibility, without arbitrary file-size or method-length targets.
- Default implementation classes to `internal sealed`. Use public types for real cross-assembly contracts and
  deliberate registration APIs; preserve required inheritance and framework signatures.
- Keep transport binding and response mapping at the edge, application decisions in the handler, and storage or
  external-tool details behind existing ports. Keep one minimal HTTP endpoint per file and compose related
  endpoints through a feature route-group extension.
- Extract a helper when it names a coherent operation, owns a resource/lifecycle, or removes meaningful duplication.
  Keep trivial, single-use logic inline when extraction would only make the reader jump between files.
- Remove superseded helpers, imports and dependencies after checking callers. Retain comments that explain a
  decision, constraint or invariant; omit narration of the next statement and empty documentation scaffolding.

## Make the execution easy to follow

- Use file-scoped namespaces, Allman braces, four spaces, `_camelCase` private fields and purpose-specific names.
  Avoid generic `Service`/`DTO` suffixes. Custom asynchronous methods omit `Async` where the repository follows
  that convention; framework overrides and interface implementations keep required names.
- Separate setup, validation, work and the final return with meaningful blank lines. Wrap long arguments and
  initializer members individually; put successive LINQ, EF and DI calls on separate lines.
- Prefer `var`, `nameof`, pattern matching and accurate nullable annotations. Use clear guards and named intermediate
  values when nested expressions hide a decision. Keep simple expressions and assertions compact.
- Validate external input and public boundaries. Avoid redundant defensive checks inside a guaranteed contract.
  Preserve expected error mapping, unexpected exceptions and cancellation.

## Preserve behavior during cleanup

Identify the invariants before moving code: result values, ordering, identities, serialization, transaction
ownership, scope lifetime and disposal. Inspect changed call sites. A shorter method can still change when a
query executes, when a resource is released, or which error a caller receives.

Do not introduce a mediator, generic repository, unit of work, wrapper layer or new dependency solely for stylistic
uniformity. Do not alter public contracts, generated clients or historical migrations as incidental styling.
Keep unrelated cleanup out of the diff.

Finish by reading the changed code as a maintainer would, running the existing formatter and relevant checks.
For a behavior change, use meaningful coverage at the affected boundary. Formatting alone needs no new tests.
Report what was improved and what was actually verified; do not equate a formatter pass with correctness.
