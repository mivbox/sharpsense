using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpSense.Application.Features.Indexing.IndexTarget;
using SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<ICommandHandler<IndexTargetCommand>, IndexTargetCommandHandler>();
        services.TryAddTransient<ICommandHandler<UpdateWorkspaceFilesCommand>, UpdateWorkspaceFilesCommandHandler>();
        return services;
    }
}
