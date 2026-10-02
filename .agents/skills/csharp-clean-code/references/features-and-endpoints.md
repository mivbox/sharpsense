# Features and endpoints

Read this when changing application handlers, dependency registration or minimal HTTP endpoints. The examples
apply standards for responsibility, visibility and composition. Keep the repository's existing dispatch and error
contracts; do not add infrastructure or dependencies solely to reproduce an example.

## One operation per endpoint file

A `GetNoteEndpoint.cs` file owns mapping and its named handler:

~~~csharp
internal static class GetNoteEndpoint
{
    public static IEndpointRouteBuilder MapGetNoteEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{noteId:guid}", GetNote)
            .WithName(nameof(GetNote))
            .ProducesProblem(StatusCodes.Status404NotFound);

        return builder;
    }

    private static async Task<Results<Ok<NoteResponse>, ProblemHttpResult>> GetNote(
        Guid noteId,
        IQueryHandler<GetNoteQuery, Result<Note>> handler,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await handler.Handle(new GetNoteQuery(noteId), ct);
        if (result.IsFailed)
        {
            return NoteProblemResults.From(result, context);
        }

        var response = NoteResponseMapper.Map(result.Value);

        return TypedResults.Ok(response);
    }
}
~~~

This excerpt assumes existing request/response contracts, mapping and problem handling. `NoteProblemResults`
stands for the project's shared mapping policy; do not add a per-endpoint wrapper solely to match this name.
Declare the error responses the real implementation can return. Preserve OpenAPI operation names and metadata
when moving an existing endpoint; regenerate clients through the existing workflow if the contract changes.

A `NotesEndpointRouteBuilderExtensions.cs` file composes the feature:

~~~csharp
internal static class NotesEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapNoteEndpoints(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGroup("/api/notes")
            .WithTags("Notes")
            .MapGetNoteEndpoint()
            .MapCreateNoteEndpoint();

        return builder;
    }
}
~~~

Keep endpoint implementation types internal when composition stays in one assembly. Make an entry point public
when another assembly consumes it. Put top-level request/response types in separate, appropriately named files.

## Handler and registration boundaries

A handler owns the operation's decisions and coordinates existing ports. An endpoint binds inputs, invokes the
handler and maps its result. A repository owns its established query/transaction implementation. Avoid putting
EF context access in the endpoint or moving a transaction between layers during a readability change.

Use the existing registration convention:

~~~csharp
services.TryAddTransient<ICommandHandler<RenameNoteCommand, Result<Note>>, RenameNoteCommandHandler>();
~~~

One clear registration needs no chain or wrapper. Register a coherent feature through its existing feature
extension and compose features at the host. Do not introduce an extra dispatcher around direct handlers.

When reviewing a split, verify routes, serializers, generated operation IDs, service lifetimes, cancellation and
disposal at the real boundary. An isolated handler test cannot prove host configuration or HTTP error mapping.
