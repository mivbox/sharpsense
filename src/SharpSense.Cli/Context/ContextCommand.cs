using FluentResults;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Context360;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Memory;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace SharpSense.Cli.Context;

[UsedImplicitly]
internal sealed class ContextCommand : AbstractAsyncCommand<ContextCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--node-id <NODE_ID>")]
        public int NodeId { get; init; }

        [CommandOption("--toon")]
        [Description("Use compact TOON output instead of JSON.")]
        public bool UseToonFormat { get; init; }

        [CommandOption("--include-memories")]
        public bool IncludeMemories { get; init; }

        public override ValidationResult Validate()
            => NodeId <= 0
                ? ValidationResult.Error("A positive node id is required.")
                : base.Validate();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddContext360();
        services.AddContext360Infrastructure();
        services.AddMemory();
        services.AddMemoryInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var contextHandler = services.GetRequiredService<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>();
        var result = await contextHandler.Handle(
            new GetNodeContextQuery(
                settings.NodeId,
                10),
            ct);

        if (result.IsFailed)
        {
            CommandOutput.WriteError(context, string.Join("; ", result.Errors.Select(error => error.Message)));

            return 1;
        }

        Domain.KnowledgeGraph.Nodes.MemoryNode[]? semanticContext = null;
        if (settings.IncludeMemories)
        {
            var memoryRepository = services.GetRequiredService<IMemoryRepository>();
            var memoriesByNodeId = await memoryRepository.GetNodeMemories([settings.NodeId], intents: null, ct);
            semanticContext = memoriesByNodeId.TryGetValue(settings.NodeId, out var records)
                ? records
                : [];
        }

        var nodeContext = result.Value;
        var output = settings.UseToonFormat
            ? TokenObjectNotation.SerializeContext360(nodeContext, semanticContext)
            : JsonSerializer.Serialize(
                new
                {
                    nodeContext.TargetNode,
                    nodeContext.Callers,
                    nodeContext.Implementers,
                    nodeContext.Callees,
                    nodeContext.Inherits,
                    nodeContext.Parents,
                    nodeContext.Children,
                    Memories = semanticContext?.Select(static memory => new
                    {
                        memory.Id,
                        memory.Intent,
                        memory.IsStale,
                        memory.Tags
                    })
                },
                CliJsonOptions.Default);

        CommandOutput.Write(context, output);

        return 0;
    }
}
