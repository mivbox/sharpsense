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

## Match the surrounding C#

- Default implementations to `internal sealed`; retain inheritance where it has a purpose. Public types should
  serve cross-assembly contracts, transport models, shared options/value APIs or registration entry points.
- Use file-scoped namespaces, Allman braces, `_camelCase` private fields and meaningful blank lines. Follow
  `.editorconfig` and the repository's naming conventions, including its custom async-method convention.
- Put wrapped arguments and initializer members on separate lines. Put successive LINQ, EF, DI and Moq calls on
  separate lines. Keep short assertions readable; use named locals when nested expressions obscure the decision.
- Preserve comments explaining a decision or invariant. Remove obsolete callers and dead branches when replacing
  an implementation. Extract helpers when they make the operation easier to understand.
- Use the repository's established result/error contract for expected failures. Keep transport mapping outside
  the handler, and preserve unexpected failures and cancellation.

A typical registration shape, adapted to the local interfaces and service lifetime:

```csharp
services.TryAddTransient<ICommandHandler<DeleteItemCommand, Result>, DeleteItemCommandHandler>();
```

Review the final diff for inconsistent formatting, accidental public APIs, unnecessary indirection and lost
explanations. Run the affected build/tests and existing formatter checks as appropriate to the change.
