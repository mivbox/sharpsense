using JetBrains.Annotations;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Serilog;
using SharpSense.Application.DependencyGraph;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges.Models;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Memory;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.WorkspaceExplorer;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.WorkspaceExplorer;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Ui;

internal sealed class UiCommand : AbstractWebAsyncCommand<UiCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<UiCommand>();
    private const string _namespace = "SharpSense.UI";

    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--url <url>")]
        public string Url { get; set; } = "http://localhost:50069";

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }
    }

    protected override void ConfigureServices(Settings settings, IServiceCollection services)
    {
        var repositoryRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        services.AddRepositoryWorkspace(repositoryRoot);
        services.AddSharpSenseConfiguration(repositoryRoot);

        services.AddDependencyGraph();
        services.AddDependencyGraphInfrastructure();
        services.AddWorkspaceExplorer();
        services.AddWorkspaceExplorerInfrastructure();
        services.AddMemory();
        services.AddMemoryInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddPersistence();
    }

    protected override void ConfigureApp(Settings settings, WebApplication app)
    {
        Logger.Information(
            "Configuring UI application for {Url} with embedded asset namespace {EmbeddedNamespace}",
            settings.Url,
            _namespace);

        app.Urls.Add(settings.Url);

        var fileProvider = new ManifestEmbeddedFileProvider(typeof(UiCommand).Assembly, _namespace);

        app.MapGet(
            "/api/tree",
            static async Task<WorkspaceTreeResult> (
                string? path,
                IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult> handler,
                CancellationToken ct) =>
                await handler.Handle(new GetWorkspaceTreeQuery(string.IsNullOrWhiteSpace(path) ? "/" : path), ct))
            .AllowAnonymous();

        app.MapGet(
            "/api/graph/nodes",
            static async Task<IAsyncEnumerable<GraphNode>> (
                int[]? directoryIds,
                IQueryHandler<GetDependencyGraphNodesQuery, IAsyncEnumerable<GraphNode>> handler,
                CancellationToken ct) =>
                await handler.Handle(new GetDependencyGraphNodesQuery(directoryIds ?? []), ct))
            .AllowAnonymous();

        app.MapGet(
            "/api/graph/edges",
            static async Task<IAsyncEnumerable<GraphEdge>> (
                int[]? directoryIds,
                IQueryHandler<GetDependencyGraphEdgesQuery, IAsyncEnumerable<GraphEdge>> handler,
                CancellationToken ct) =>
                await handler.Handle(new GetDependencyGraphEdgesQuery(directoryIds ?? []), ct))
            .AllowAnonymous();

        // Memory: list inline metadata for one node (id + intent + tags + stale flag, no content).
        app.MapGet(
                "/api/memory/node/{nodeId:int}",
                static async Task<IResult> (
                    int nodeId,
                    IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> handler,
                    string? intents,
                    CancellationToken ct) =>
                {
                    MemoryIntent[]? intentFilter = null;
                    if (!string.IsNullOrWhiteSpace(intents))
                    {
                        intentFilter = intents
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(raw => Enum.Parse<MemoryIntent>(raw, ignoreCase: true))
                            .ToArray();
                    }

                    var result = await handler.Handle(new GetNodeMemoriesQuery(nodeId, intentFilter), ct);
                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : Results.BadRequest(new { error = string.Join("; ", result.Errors) });
                })
            .AllowAnonymous();

        // Memory: fetch the full content of a single memory by id.
        app.MapGet(
                "/api/memory/{memoryId:guid}",
                static async Task<IResult> (
                    Guid memoryId,
                    IQueryHandler<GetMemoryQuery, Result<MemoryNode>> handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.Handle(new GetMemoryQuery(memoryId), ct);
                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : Results.NotFound(new { error = string.Join("; ", result.Errors) });
                })
            .AllowAnonymous();

        // Memory: batch fetch by id list.
        app.MapGet(
                "/api/memory",
                static async Task<IResult> (
                    Guid[]? ids,
                    IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>> handler,
                    CancellationToken ct) =>
                {
                    if (ids is null || ids.Length == 0)
                    {
                        return Results.Ok(Array.Empty<MemoryNode>());
                    }

                    var result = await handler.Handle(new GetMemoriesQuery(ids), ct);
                    return result.IsSuccess
                        ? Results.Ok(result.Value.Values)
                        : Results.BadRequest(new { error = string.Join("; ", result.Errors) });
                })
            .AllowAnonymous();

        // Memory: add a new memory to a node.
        app.MapPost(
                "/api/memory/node/{nodeId:int}",
                static async Task<IResult> (
                    int nodeId,
                    AddMemoryRequest request,
                    ICommandHandler<AttachMemoryCommand, Result> handler,
                    CancellationToken ct) =>
                {
                    var intent = string.IsNullOrWhiteSpace(request.Intent)
                        ? MemoryIntent.Convention
                        : Enum.Parse<MemoryIntent>(request.Intent, ignoreCase: true);

                    var command = new AttachMemoryCommand(
                        nodeId,
                        request.Content,
                        request.Tags ?? [],
                        intent);
                    var result = await handler.Handle(command, ct);
                    return result.IsSuccess
                        ? Results.Ok(new { ok = true, nodeId, intent = intent.ToString() })
                        : Results.BadRequest(new { error = string.Join("; ", result.Errors) });
                })
            .AllowAnonymous();

        // Memory: remove a memory by id.
        app.MapDelete(
                "/api/memory/{memoryId:guid}",
                static async Task<IResult> (
                    Guid memoryId,
                    ICommandHandler<DeleteMemoryCommand, Result> handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.Handle(new DeleteMemoryCommand(memoryId), ct);
                    return result.IsSuccess
                        ? Results.Ok(new { ok = true, memoryId })
                        : Results.NotFound(new { error = string.Join("; ", result.Errors) });
                })
            .AllowAnonymous();

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = CreateContentTypeProvider()
        });

        app.MapFallback(
            async context =>
            {
                var indexFile = fileProvider.GetFileInfo("index.html");
                if (!indexFile.Exists)
                {
                    Logger.Error(
                        "Embedded UI asset {AssetName} was not found in namespace {EmbeddedNamespace}",
                        "index.html",
                        _namespace);

                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync(
                        "Embedded UI assets were not found. Build the frontend before running the UI command.",
                        context.RequestAborted);
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                await using var stream = indexFile.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
            });
    }

    private static FileExtensionContentTypeProvider CreateContentTypeProvider()
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider { Mappings =
            {
                [".map"] = "application/json", [".mjs"] = "text/javascript"
            }
        };
        return contentTypeProvider;
    }

    public sealed record AddMemoryRequest(string Content, string[]? Tags, string? Intent);
}
