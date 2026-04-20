using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpSense.Application.Features.Indexing.IndexSolution;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<ICommandHandler<IndexSolutionCommand>, IndexSolutionCommandHandler>();
        return services;
    }
}
