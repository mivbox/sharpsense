---
name: csharp-behavior-tests
description: Create or review C# behavior tests with xUnit, Moq and AwesomeAssertions while keeping fixtures simple and implementation details private.
---

# C# behavior tests

Identify the behavior that could regress, then find the existing test project and fixture that owns its boundary.
Use the versions, naming rules and test layout already selected by the repository.

## Pick the boundary

- Prove handler decisions through their ports. Exercise actual database, host or transport behavior through the
  corresponding integration fixture. Match the repository's project split rather than creating another convention.
- Test public contracts or supported internal boundaries via `InternalsVisibleTo`. Never use reflection, invoke
  private members, make implementations public for tests or mock the subject under test.
- Reuse existing database and host fixtures. Do not mock EF queries to prove provider behavior or introduce a
  second fixture framework for a single case.

## Keep the case readable

- Use scenario names such as `WhenCondition_ThenOutcome`, with one underscore. Separate setup, invocation and
  assertions with blank lines; follow local rules on comments without adding redundant phase labels.
- Use Moq only for dependencies the behavior needs. Direct construction and local mocks are fine. Extract common
  setup or a factory only when several cases become clearer.
- Put successive setup calls on separate lines. Keep simple assertions compact and wrap complex ones.
- Assert the result or a meaningful port interaction. Avoid assertions solely for private details, type
  assignability, static file layouts, implementation names or incidental logging.
- Preserve exact exception checks where the exception type is a contract. Pass the test cancellation token,
  isolate mutable process state and dispose resources. Synchronize concurrent work using signals or bounded
  conditions instead of a fixed delay chosen to make the test pass.

```csharp
var ct = TestContext.Current.CancellationToken;
repository
    .Setup(candidate => candidate.Delete(itemId, ct))
    .ReturnsAsync(Result.Ok());

var result = await handler.Handle(new DeleteItemCommand(itemId), ct);

result.IsSuccess.Should().BeTrue();
```

For a bug fix, demonstrate the behavioral failure before the fix when practical. Run the affected cases and explain
what they prove. Formatting-only edits do not need new tests, and removing a redundant test should preserve any
independent behavior it was covering.
