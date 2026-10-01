using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<WorkspaceExtractionCoordinator>();
        services
            .TryAddTransient<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>, IndexWorkspaceCommandHandler>();
        services
            .TryAddTransient<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>, UpdateWorkspaceFilesCommandHandler>();

        return services;
    }
}
