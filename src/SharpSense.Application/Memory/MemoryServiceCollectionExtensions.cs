using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Memory.AttachMemory;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Memory.DeleteMemory;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Memory.GetMemories;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Memory.GetMemory;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Memory.GetNodeMemories;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<ICommandHandler<AttachMemoryCommand, Result>, AttachMemoryCommandHandler>();
        services.TryAddTransient<ICommandHandler<DeleteMemoryCommand, Result>, DeleteMemoryCommandHandler>();
        services.TryAddTransient<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>, GetNodeMemoriesQueryHandler>();
        services.TryAddTransient<IQueryHandler<GetMemoryQuery, Result<MemoryNode>>, GetMemoryQueryHandler>();
        services.TryAddTransient<IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>>, GetMemoriesQueryHandler>();

        return services;
    }
}
