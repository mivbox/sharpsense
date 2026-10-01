---
name: csharp-feature-work
description: Implement or refactor C# features using the repository's CQRS handlers, vertical slices, visibility and formatting conventions.
---

# C# feature work

Read the applicable repository instructions and find a nearby maintained handler, registration method and test
that match the requested change. Use the framework, dependencies and build configuration already selected there.

## Keep the feature together

- Follow the existing feature/operation layout. Keep a command or query beside its handler, using the repository's
  local model directory. Put shared feature contracts and models in their established locations.
- Implement the existing command/query interfaces and register them through the feature's extension method.
  Keep use-case orchestration in the handler and input/result presentation in the transport adapter.
- Put storage and external I/O behind an appropriate existing boundary. Keep one-way layer dependencies; do not
  create a new mediator, generic repository or dispatch layer merely to make a small feature look uniform.
- Preserve established transaction ownership and pass cancellation through the operation.

## Implement and verify

Use the existing result/error contract for expected failures. Keep transport mapping outside the handler, and
preserve unexpected exceptions and cancellation. Follow the repository's naming, visibility and formatting
conventions.

For production readability and responsibility cleanup, the companion
[csharp-clean-code](../csharp-clean-code/SKILL.md) provides focused examples. For regression coverage, use
[csharp-behavior-tests](../csharp-behavior-tests/SKILL.md). Read the guidance needed for the task rather than loading
all references.

Review the final diff for changed contracts, scope ownership and unnecessary indirection. Run affected
build/tests and the existing formatter checks. Do not introduce architecture changes merely to match an example.
