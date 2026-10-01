using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Context360;
using SharpSense.Application.GraphStats;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.Inheritors;
using SharpSense.Application.Trace;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.Trace;
using System.Text.Json.Serialization;

namespace SharpSense.Cli.Ui.Api;

internal static class UiApiServiceCollectionExtensions
{
    public static IServiceCollection AddUiApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddOperationTransformer(WorkspaceApiContract.DescribeWorkspaceHeader));
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        });
        services.AddContext360()
            .AddContext360Infrastructure();
        services.AddGraphStats()
            .AddGraphStatsInfrastructure();
        services.AddHybridSearch()
            .AddHybridSearchInfrastructure();
        services.AddImpactAnalysis()
            .AddImpactAnalysisInfrastructure();
        services.AddInheritors()
            .AddInheritorsInfrastructure();
        services.AddTrace()
            .AddTraceInfrastructure();

        return services;
    }
}
