---
name: csharp-behavior-tests
description: Write, improve or review C# behavior tests in the user's ServiceKit style using xUnit, Moq and AwesomeAssertions. Use for regression coverage, meaningful assertions, readable fixtures and test cleanup; production-code layout belongs to csharp-clean-code.
---

# C# behavior tests

Start with the behavior and the mistake the test should catch. Read the repository's instructions, the nearest
maintained test and the fixture for that boundary. Preserve its framework versions, project layout and supported
test access. The examples capture the user's style without requiring a ServiceKit checkout.

## Load examples when needed

- Read [Behavior and assertions](references/behavior-and-assertions.md) when choosing a boundary or strengthening
  cases that currently prove little.
- Read [Fixtures and lifetimes](references/fixtures-and-lifetimes.md) when arranging shared setup, asynchronous work,
  native resources, host tests or process tests.

Read the relevant reference; an ordinary pure-function assertion does not require the host/lifecycle guidance.

## Prove an observable outcome

- State a concrete trigger and result. Ask which plausible wrong implementation would still pass the proposed
  assertions; improve the scenario when a no-op, wrong item, missing filter or wrong order could pass.
- Assert values, identities, error semantics or persisted effects that matter. Ranking needs an order assertion;
  filtering needs an excluded candidate; deletion needs an existing target; isolation needs two distinct scopes.
  Assert setup success before using a returned identity or comparing before/after state.
- Exercise transitions when state is involved: existing data followed by deletion, success followed by failure,
  failure followed by recovery, or cancellation during active work. Choose the transitions relevant to the change;
  do not generate every possible permutation.
- Test handler decisions through their ports. Use real-provider fixtures for SQL, transactions and migrations;
  real host/transport fixtures for routing, configuration, error mapping and process behavior.
- Test public or supported internal boundaries. Never reflect over private members, mock the subject under test,
  make implementations public for tests, or assert interface assignability, assembly names or static file layouts.
- Verify port interactions when they express a behavior such as no write after failed validation, an emitted
  notification, or bounded retry. Avoid blanket `VerifyAll` and internal call-order assertions.

## Write tests in the user's style

- Name each test `WhenCondition_ThenOutcome` with one underscore. Use meaningful scenario names, not
  `Works`, `Successful` or `PropertiesAreSetCorrectly` without saying which outcome matters.
- Separate setup, the action and assertions with whitespace. Omit Arrange/Act/Assert comments.
- Keep inputs and expected values visible in the case. Use direct construction or a small local factory; add
  shared setup only when it removes meaningful repeated work. Do not hide the assertion inside a fixture.
- Wrap long constructor/setup arguments and initializer members individually. Put successive Moq calls on
  separate lines. Keep simple AwesomeAssertions expressions on one line and use named values for complicated predicates. Use `ThrowExactly`/`ThrowExactlyAsync` where exception type is a contract.
- Use theories for one rule across several inputs with readable expected values. Separate tests when setup,
  meaning or outcome differs. Avoid a large scenario containing several independent behaviors.

## Own the test's state

Pass `TestContext.Current.CancellationToken` to asynchronous operations. Reuse disposable database, host and
process fixtures, isolate temporary state and avoid the developer's real catalog. Restore process-global state
and disable parallel peers where necessary. Synchronize races with signals or bounded observable conditions,
not an arbitrary sleep. Keep cleanup reliable after a failed assertion or cancellation.

For a bug fix, demonstrate failure before the fix when practical. Inspect both the returned outcome and the
relevant effect. For a review, retain valuable existing coverage and replace weak cases only when their useful
behavior remains covered. A pass-through handler already exercised through a transport need not gain another
test that merely returns its mocked result.

Run the affected tests and report what they prove, along with any untested boundary. Formatting-only edits do
not need new tests. Never weaken assertions, skip cases or change production visibility just to obtain a pass.
