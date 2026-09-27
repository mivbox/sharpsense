using FluentResults;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Memory;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;

namespace SharpSense.Cli.Memory;

[UsedImplicitly]
internal sealed class MemoryCommand : AbstractAsyncCommand<MemoryCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<action>")]
        public string Action
        {
            get; init;
        } = string.Empty;

        [CommandOption("--node-id <NODE_ID>")]
        public int? NodeId
        {
            get; init;
        }

        [CommandOption("--memory-id <MEMORY_ID>")]
        public Guid? MemoryId
        {
            get; init;
        }

        [CommandOption("--memory-ids <MEMORY_IDS>")]
        public Guid[]? MemoryIds
        {
            get; init;
        }

        [CommandOption("--content <CONTENT>")]
        public string? Content
        {
            get; init;
        }

        [CommandOption("--tag <TAG>")]
        public string[]? Tags
        {
            get; init;
        }

        [CommandOption("--intent <INTENT>")]
        public string? IntentRaw
        {
            get; init;
        }

        [CommandOption("--intent-filter <INTENT>")]
        public string[]? IntentFilterRaw
        {
            get; init;
        }

        public override ValidationResult Validate()
        {
            if (!string.Equals(Action, "add", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Action, "remove", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Action, "list", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Action, "get", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Error("Action must be one of: add, remove, list, get.");
            }

            if (string.Equals(Action, "add", StringComparison.OrdinalIgnoreCase)
                && (NodeId is null || string.IsNullOrWhiteSpace(Content)))
            {
                return ValidationResult.Error("`add` requires --node-id and --content.");
            }

            if (string.Equals(Action, "remove", StringComparison.OrdinalIgnoreCase) && MemoryId is null)
            {
                return ValidationResult.Error("`remove` requires --memory-id.");
            }

            if (string.Equals(Action, "list", StringComparison.OrdinalIgnoreCase) && NodeId is null)
            {
                return ValidationResult.Error("`list` requires --node-id.");
            }

            if (string.Equals(Action, "get", StringComparison.OrdinalIgnoreCase)
                && MemoryId is null
                && (MemoryIds is null || MemoryIds.Length == 0))
            {
                return ValidationResult.Error("`get` requires --memory-id or --memory-ids.");
            }

            if (!string.IsNullOrWhiteSpace(IntentRaw) && !TryParseIntent(IntentRaw, out _))
            {
                return ValidationResult.Error("--intent must be one of: Convention, Invariant, Todo, Warning, Decision.");
            }

            if (IntentFilterRaw is { Length: > 0 }
                && IntentFilterRaw.Any(raw => !TryParseIntent(raw, out _)))
            {
                return ValidationResult.Error("--intent-filter values must be one of: Convention, Invariant, Todo, Warning, Decision.");
            }

            return base.Validate();
        }

        private static bool TryParseIntent(string raw, out SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent intent)
            => Enum.TryParse(raw, ignoreCase: true, out intent);
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddMemory();
        services.AddMemoryInfrastructure();
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

        return settings.Action.ToLowerInvariant() switch
        {
            "add" => await AddMemory(context, settings, services, ct),
            "remove" => await RemoveMemory(context, settings, services, ct),
            "list" => await ListMemories(context, settings, services, ct),
            "get" => await GetMemory(context, settings, services, ct),
            _ => Fail(
                context,
                $"Unknown action '{settings.Action}'.")
        };
    }

    private static async Task<int> AddMemory(CommandContext context, Settings settings, IServiceProvider services, CancellationToken ct)
    {
        var handler = services.GetRequiredService<ICommandHandler<AttachMemoryCommand, Result>>();
        var intent = string.IsNullOrWhiteSpace(settings.IntentRaw)
            ? SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Convention
            : Enum.Parse<SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent>(settings.IntentRaw, ignoreCase: true);
        var result = await handler.Handle(
            new AttachMemoryCommand(settings.NodeId!.Value, settings.Content!, settings.Tags, intent),
            ct);

        if (result.IsFailed)
        {
            CommandOutput.Write(
                context,
                $"add failed: {string.Join("; ", result.Errors)}");

            return 1;
        }

        var memories = await services
            .GetRequiredService<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>()
            .Handle(new GetNodeMemoriesQuery(settings.NodeId.Value), ct);
        var memory = memories.IsSuccess && memories.Value!.Length > 0 ? memories.Value[^1] : null;

        var output = new StringBuilder()
            .Append("added_memory: true")
            .AppendLine()
            .Append("node_id: ")
            .Append(settings.NodeId.Value)
            .AppendLine();
        if (memory is not null)
        {
            output
                .Append("memory_id: ")
                .Append(memory.Id)
                .AppendLine()
                .Append("intent: ")
                .Append(memory.Intent)
                .AppendLine()
                .Append("is_stale: ")
                .Append(memory.IsStale ? "true" : "false")
                .AppendLine()
                .Append("content: \"")
                .Append(SanitizeForOutput(memory.Content))
                .Append('"')
                .AppendLine();
        }

        CommandOutput.Write(context, output.ToString());

        return 0;
    }

    private static async Task<int> RemoveMemory(CommandContext context, Settings settings, IServiceProvider services, CancellationToken ct)
    {
        var handler = services.GetRequiredService<ICommandHandler<DeleteMemoryCommand, Result>>();
        var result = await handler.Handle(new DeleteMemoryCommand(settings.MemoryId!.Value), ct);

        if (result.IsFailed)
        {
            CommandOutput.Write(
                context,
                $"remove failed: {string.Join("; ", result.Errors)}");

            return 1;
        }

        CommandOutput.Write(
            context,
            $"removed_memory: {settings.MemoryId.Value}");

        return 0;
    }

    private static async Task<int> ListMemories(CommandContext context, Settings settings, IServiceProvider services, CancellationToken ct)
    {
        SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent[]? intentFilter = null;
        if (settings.IntentFilterRaw is { Length: > 0 })
        {
            intentFilter = settings.IntentFilterRaw
                .Select(static raw => Enum.Parse<SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent>(raw, ignoreCase: true))
                .ToArray();
        }

        var memoriesResult = await services
            .GetRequiredService<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>()
            .Handle(new GetNodeMemoriesQuery(settings.NodeId!.Value, intentFilter), ct);

        if (memoriesResult.IsFailed)
        {
            CommandOutput.Write(
                context,
                $"list failed: {string.Join("; ", memoriesResult.Errors)}");

            return 1;
        }

        var memories = memoriesResult.Value!;
        var builder = new StringBuilder()
            .Append("node_id: ")
            .Append(settings.NodeId.Value)
            .AppendLine()
            .Append("count: ")
            .Append(memories.Length)
            .AppendLine();
        foreach (var memory in memories)
        {
            builder
                .Append("- memory_id: ")
                .Append(memory.Id)
                .AppendLine()
                .Append("  intent: ")
                .Append(memory.Intent)
                .AppendLine()
                .Append("  is_stale: ")
                .Append(memory.IsStale ? "true" : "false")
                .AppendLine()
                .Append("  tags: [")
                .Append(string.Join(", ", memory.Tags))
                .Append(']')
                .AppendLine()
                .Append("  content: \"")
                .Append(SanitizeForOutput(memory.Content))
                .Append('"')
                .AppendLine();
        }

        CommandOutput.Write(context, builder.ToString());

        return 0;
    }

    private static async Task<int> GetMemory(CommandContext context, Settings settings, IServiceProvider services, CancellationToken ct)
    {
        // Batch path: --memory-ids id1,id2,id3 → one round-trip, one combined block.
        if (settings.MemoryIds is { Length: > 0 })
        {
            var batchHandler = services.GetRequiredService<IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>>>();
            var batchResult = await batchHandler.Handle(new GetMemoriesQuery(settings.MemoryIds), ct);
            if (batchResult.IsFailed)
            {
                CommandOutput.Write(
                    context,
                    $"get failed: {string.Join("; ", batchResult.Errors)}");

                return 1;
            }

            CommandOutput.Write(context, TokenObjectNotation.SerializeMemories(batchResult.Value!));

            return 0;
        }

        var handler = services.GetRequiredService<IQueryHandler<GetMemoryQuery, Result<MemoryNode>>>();
        var result = await handler.Handle(new GetMemoryQuery(settings.MemoryId!.Value), ct);

        if (result.IsFailed)
        {
            CommandOutput.Write(
                context,
                $"get failed: {string.Join("; ", result.Errors)}");

            return 1;
        }

        CommandOutput.Write(context, TokenObjectNotation.SerializeMemory(result.Value!));

        return 0;
    }

    private static int Fail(CommandContext context, string message)
    {
        CommandOutput.Write(context, message);

        return 1;
    }

    private static string SanitizeForOutput(string content)
        => content.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
