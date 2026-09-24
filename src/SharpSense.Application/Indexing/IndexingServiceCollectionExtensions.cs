using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<WorkspaceExtractionCoordinator>();
        services.TryAddTransient<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>, IndexTargetCommandHandler>();
        services.TryAddTransient<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>, UpdateWorkspaceFilesCommandHandler>();
        return services;
    }
}
