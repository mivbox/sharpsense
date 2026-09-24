using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Ui.Api;

public sealed record AddMemoryRequest(string Content, string[]? Tags, MemoryIntent Intent = MemoryIntent.Convention);
public sealed record AddMemoryResponse(bool Ok, int NodeId, MemoryIntent Intent);
public sealed record DeleteMemoryResponse(bool Ok, Guid MemoryId);

internal static class MemoryEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/memory").WithTags("Memory");
        group.MapGet("/node/{nodeId:int}", async (int nodeId, string? intents, IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> handler, CancellationToken ct) =>
        {
            if (nodeId <= 0) return UiApiExtensions.Invalid("nodeId must be positive.");
            var filter = new List<MemoryIntent>();
            foreach (var raw in (intents ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse<MemoryIntent>(raw, true, out var intent) || !Enum.IsDefined(intent))
                    return UiApiExtensions.Invalid($"Unknown memory intent '{raw}'.");
                filter.Add(intent);
            }
            var result = await handler.Handle(new GetNodeMemoriesQuery(nodeId, filter.Count == 0 ? null : filter.ToArray()), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : UiApiExtensions.Failure(result.Errors);
        }).WithName("GetNodeMemories").Produces<MemoryNode[]>().ProducesProblem(400);

        group.MapGet("/{memoryId:guid}", async (Guid memoryId, IQueryHandler<GetMemoryQuery, Result<MemoryNode>> handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new GetMemoryQuery(memoryId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : UiApiExtensions.Failure(result.Errors, 404);
        }).WithName("GetMemory").Produces<MemoryNode>().ProducesProblem(404);

        group.MapGet("", async (Guid[]? ids, IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>> handler, CancellationToken ct) =>
        {
            if (ids is not { Length: > 0 }) return Results.Ok(Array.Empty<MemoryNode>());
            if (ids.Length > 200) return UiApiExtensions.Invalid("At most 200 memory ids may be requested.");
            var result = await handler.Handle(new GetMemoriesQuery(ids), ct);
            return result.IsSuccess ? Results.Ok(result.Value.Values.ToArray()) : UiApiExtensions.Failure(result.Errors);
        }).WithName("GetMemories").Produces<MemoryNode[]>().ProducesProblem(400);

        group.MapPost("/node/{nodeId:int}", async (int nodeId, AddMemoryRequest request, ICommandHandler<AttachMemoryCommand, Result> handler, CancellationToken ct) =>
        {
            if (nodeId <= 0 || string.IsNullOrWhiteSpace(request.Content) || !Enum.IsDefined(request.Intent))
                return UiApiExtensions.Invalid("A positive nodeId, non-empty content, and valid intent are required.");
            var result = await handler.Handle(new AttachMemoryCommand(nodeId, request.Content, request.Tags ?? [], request.Intent), ct);
            return result.IsSuccess ? Results.Ok(new AddMemoryResponse(true, nodeId, request.Intent)) : UiApiExtensions.Failure(result.Errors);
        }).WithName("AddMemory").Produces<AddMemoryResponse>().ProducesProblem(400).ProducesProblem(404);

        group.MapDelete("/{memoryId:guid}", async (Guid memoryId, ICommandHandler<DeleteMemoryCommand, Result> handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new DeleteMemoryCommand(memoryId), ct);
            return result.IsSuccess ? Results.Ok(new DeleteMemoryResponse(true, memoryId)) : UiApiExtensions.Failure(result.Errors, 404);
        }).WithName("DeleteMemory").Produces<DeleteMemoryResponse>().ProducesProblem(404);
    }
}
