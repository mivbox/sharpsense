# Behavior and assertions

Use these patterns to decide whether a test detects a meaningful mistake. Names and contracts in snippets are
illustrative; use the repository's real fixture and APIs rather than building a new fixture DSL for the examples.

## Readable result assertions

A filtering test should fail if filtering is removed or the wrong records are returned:

~~~csharp
[Fact]
public async Task WhenListingPublishedNotes_ThenExcludesDrafts()
{
    var ct = TestContext.Current.CancellationToken;
    await using var fixture = new NoteRepositoryFixture();
    var published = await fixture.AddNote("Release guide", isPublished: true, ct);
    await fixture.AddNote("Unfinished guide", isPublished: false, ct);

    var notes = await fixture.Repository.ListPublished(ct);

    notes.Select(note => note.Id).Should().Equal(published.Id);
    notes.Single().Title.Should().Be("Release guide");
}
~~~

This assumes an existing repository fixture with real persistence. `NotBeNull` or `HaveCount(1)` alone could pass
for the wrong note. Exact identities and a distinct excluded candidate prove the selection. For ranking, use
`Equal` on identities in the expected order; unordered equivalence does not prove ranking.

Keep expected values written independently of the production calculation. Do not reproduce the same algorithm
in the assertion or derive expected output from the result under test.

## A mock should expose a decision

Use a real handler and mock only its dependency. A rejected rename should leave stored state untouched:

~~~csharp
[Fact]
public async Task WhenNoteDoesNotExist_ThenRenameFailsWithoutSaving()
{
    var ct = TestContext.Current.CancellationToken;
    var noteId = Guid.NewGuid();
    var notes = new Mock<INoteRepository>();
    notes
        .Setup(repository => repository.Find(noteId, ct))
        .ReturnsAsync((Note?)null);
    var handler = new RenameNoteCommandHandler(notes.Object);

    var result = await handler.Handle(new RenameNoteCommand(noteId, "New title"), ct);

    result.IsFailed.Should().BeTrue();
    result.Errors.Should().ContainSingle()
        .Which.Should().BeOfType<ServiceError>()
        .Which.Code.Should().Be(ServiceErrorCode.NotFound);
    notes.Verify(
        repository => repository.Save(It.IsAny<Note>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
~~~

Adapt the error property to the real contract. Here `Save` not being called is meaningful: failure must not mutate
state. Avoid verifying every read or constructor call. To prove which record was saved, capture it and assert
its identity and changed values visibly after the action; avoid hiding several expectations in `It.Is`.

A test that configures `Delete` to return success and only asserts the forwarding handler returns success usually
adds little when the operation already has transport coverage. Prefer a persisted lifecycle or an uncovered
mapping/decision instead.

## Show that a change actually happened

For deletion, create the target and an unrelated record. Verify creation succeeded, invoke deletion, then read
through a fresh context or supported query. Assert that the target disappeared and the other record survived.
This catches no-op deletion, deletion of the wrong identity and deletion of the entire collection.

For an update, establish the old value, apply the operation and read the saved value independently. Do not prove
persistence using only the entity already tracked by the writer.

For a cache or index, test a relevant sequence such as a successful read followed by source removal. Starting
with a missing item does not prove old cached data is discarded.

## Pick the boundary that can fail

| Claimed behavior | Boundary and evidence |
| --- | --- |
| Handler rejects invalid input | Real handler, returned failure and absence of forbidden side effects |
| In-memory filtering or ranking | Real calculation or handler with representative included/excluded inputs; a port may supply them |
| Database query, SQL translation or persistence | Existing real-provider fixture and representative stored data |
| HTTP preserves error details | Real route/middleware/client path and the received status/body |
| CLI configuration selects storage | Child process with isolated state and actual resulting paths |
| MCP exposes a diagnostic before database initialization | Real host startup and protocol response |
| Cancellation stops work | Active operation, observed cancellation and released resource |

Calling a tool method directly is useful for its own behavior, but it does not exercise the host that constructs
it. Label mocked, in-process and installed-package coverage accurately.

## Keep the suite useful

Preserve one clear reason for each test. A theory can cover several inputs for the same rule, with expected
values visible in each row. Do not retain duplicate cases merely because their names differ. When replacing a
weak test, identify what its removal would stop checking before deciding the new case covers it.
