# Readable C#

Use these examples for local code presentation and helper decisions. They illustrate the user's preferences,
rather than prescribing a new framework or requiring every existing file to be rewritten.

## Separate the phases

A method should expose its decisions without making the reader decode nested calls. In a repository using
FluentResults and direct command handlers, a small operation can read like this:

~~~csharp
public async Task<Result<Note>> Handle(
    RenameNoteCommand command,
    CancellationToken ct)
{
    var note = await _notes.Find(command.NoteId, ct);
    if (note is null)
    {
        return Result.Fail<Note>(new ServiceError(ServiceErrorCode.NotFound));
    }

    var renamed = note with
    {
        Title = command.Title
    };

    var saveResult = await _notes.Save(renamed, ct);
    if (saveResult.IsFailed)
    {
        return saveResult.ToResult<Note>();
    }

    return Result.Ok(renamed);
}
~~~

The types are illustrative; use the project's actual validation and error constructors. Keep a failure from
falling through to success, and keep the cancellation token visible. The intermediate `renamed` value earns its
place by naming the state being saved. A local `result` variable followed immediately by `return result` is useful
when it separates a substantial operation; do not require it for every trivial return.

Prefer a guard to several nested success branches. A short conditional or switch expression is fine when its
cases are easy to compare; do not replace clear expressions merely to enforce a single syntax.

## Fluent calls and arguments

~~~csharp
var summaries = notes
    .Where(note => note.IsVisible)
    .OrderBy(note => note.Title)
    .ThenBy(note => note.Id)
    .Select(note => new NoteSummary(note.Id, note.Title))
    .ToArray();

await _notifications.Publish(
    note.Id,
    change.Description,
    ct);

return summaries;
~~~

Keep successive calls on separate lines and wrap long argument lists one argument per line. Use a lambda name
that makes the predicate readable. Do not move filtering from SQL to memory, change ordering, or materialize a
deferred sequence earlier just to achieve this layout.

Simple members remain compact:

~~~csharp
public string Name { get; init; } = string.Empty;
~~~

Use ordinary constructors or primary constructors to match the nearest maintained example. Do not run a
constructor-style conversion across unrelated files. Explicit private fields use `_camelCase`.

## Choose a useful helper

Extract `ResolveDestination` if it owns path validation and normalization reused by multiple callers.
Extract a disposable operation scope if it clarifies who owns resources.

Keep `return items.Count == 0;` inline unless a domain name communicates something beyond the expression.
Do not replace it with a generic helper, introduce an interface for a single pure calculation, or create an
options class for values that are used together only once.

When splitting a large class, first identify independently understandable responsibilities. Preserve state
ownership and ordering across the split. Several partial files containing the same mixed responsibilities
do not improve the boundary.

## Names and comments

Use the operation's name: `WorkspaceCatalog`, `MemoryStore`, `GetNoteEndpoint`. Keep framework-mandated names
such as `SaveChangesAsync` when implementing their contract.

Explain a non-obvious constraint:

~~~csharp
// Keep the previous snapshot until the replacement commits so readers never see a partial graph.
~~~

Avoid comments such as `// Save the note` above `Save`. Public documentation should explain input, output or
failure semantics that a caller needs, rather than reproduce the signature.

## A useful final read

Can a maintainer find the entry point, decisions, side effects and returned value without following trivial
wrappers? Are visibility and nullability accurate? Did cleanup preserve enumeration timing and resource lifetime?
Check the actual diff, including newly created files; compiler and formatter success answer different questions.
