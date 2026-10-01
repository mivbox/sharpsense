# Fixtures and lifetimes

Read this when a case needs meaningful shared setup, asynchronous work, databases, hosts or processes.
Use the existing fixture for that boundary. The examples are shapes to adapt, not a request for a new test framework.

## Keep setup proportional

A pure function usually needs local inputs and assertions. A handler with two ports usually needs two local
mocks and direct construction. A database lifecycle benefits from a fixture that owns connections and cleanup.

Use a small factory when several cases repeat the same construction. Keep behavior-specific values in the
test. Do not add a base class, generic fixture builder or large object graph to save a few obvious lines.

A nested `Fixture` is appropriate when it is specific to one test class. Move shared setup only when other cases
need the same ownership model. Avoid a `TargetFactory` property that silently creates a different subject on
each access when the test relies on state persisting between actions.

## Keep assertions where the reader expects them

~~~csharp
var ct = TestContext.Current.CancellationToken;
await using var fixture = new NotesDatabaseFixture();
var retained = await fixture.CreateNote("Retain", ct);
var removed = await fixture.CreateNote("Remove", ct);

var result = await fixture.Handler.Handle(new DeleteNoteCommand(removed.Id), ct);

result.IsSuccess.Should().BeTrue();
var savedNotes = await fixture.ReadWithNewContext(ct);
savedNotes.Select(note => note.Id).Should().Equal(retained.Id);
~~~

This uses an existing fixture whose create operation fails clearly if setup fails. If it returns a result
instead, assert success before reading its value. Fixtures own setup and disposal; the test owns the outcome
assertions. Fresh reads prevent tracked state from impersonating persisted state.

Use the real provider when provider behavior matters. Mocking `DbSet` or using a different in-memory provider
cannot prove SQL translation, locking, migration compatibility or transactional behavior.

## Synchronize actual progress

To test cancellation during an operation, wait for a signal that the dependency started, then cancel.
Use a linked token and bounded waiting so a broken implementation fails rather than hangs.

A cancellation assertion can look like:

~~~csharp
Func<Task> waitForCompletion = () => operation;

await waitForCompletion.Should().ThrowExactlyAsync<OperationCanceledException>();
~~~

Use that exact type only if the operation promises it. A contract allowing a cancellation subtype needs the
appropriate broader assertion. Retain the repository's chosen exception contract.

For a concurrent update, pause at a supported dependency or real synchronization point, perform the competing
action, then release it and assert the result. Avoid reflection into private fields or adding a production-only
hook for one test. A bounded poll of an observable state is appropriate for an external watcher; a fixed delay
alone does not establish that indexing or shutdown finished.

## Dispose after failures too

Use `using`/`await using` for owned resources. A process fixture should drain redirected output, bound waiting,
terminate the owned process tree if needed and observe exit in cleanup. Do not kill unrelated processes.

A cancelled test token may already be unusable during cleanup. Follow the existing fixture's bounded shutdown
pattern; where cleanup needs a separate token, explain that lifetime rather than passing an already-cancelled
token blindly. Restore changed environment/global state in `finally` and isolate tests that mutate it.

Exercise relevant lifetime behavior through the real owner: a stream unsubscribes, a lease becomes available,
a child exits, or a fresh database connection can observe committed state. Asserting that `Dispose` was called
on a mock does not establish those native effects.
