using System.Text.Json.Serialization;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Context360;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.GraphStats;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.Inheritors;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Trace;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.Trace;
using SharpSense.Infrastructure.Storage;
using SharpSense.Cli.Ui.Indexing;

namespace SharpSense.Cli.Ui.Api;

internal static class UiApiExtensions
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
        services.AddContext360().AddContext360Infrastructure();
        services.AddGraphStats().AddGraphStatsInfrastructure();
        services.AddHybridSearch().AddHybridSearchInfrastructure();
        services.AddImpactAnalysis().AddImpactAnalysisInfrastructure();
        services.AddInheritors().AddInheritorsInfrastructure();
        services.AddTrace().AddTraceInfrastructure();
        return services;
    }

    public static void MapUiApi(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var status = exception switch
            {
                WorkspaceBusyException or WorkspaceIndexBusyException or WorkspaceDefinitionChangedException or GraphRevisionChangedException => 409,
                ArgumentException or BadHttpRequestException or DirectoryNotFoundException or FileNotFoundException or InvalidOperationException => 400,
                KeyNotFoundException => 404,
                _ => 500
            };
            await Results.Problem(statusCode: status, title: status == 500 ? "Request failed" : "Invalid request",
                detail: status == 500 ? "An unexpected error occurred. Check the SharpSense server log." : exception?.Message)
                .ExecuteAsync(context);
        }));
        app.UseWorkspaceRequests();
        app.UseStatusCodePages(async status =>
        {
            if (status.HttpContext.Request.Path.StartsWithSegments("/api"))
                await Results.Problem(statusCode: status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext);
        });
        app.MapOpenApi();
        WorkspaceCatalogEndpoints.Map(app);
        app.MapWorkspaceIndexingEndpoints();
        WorkspaceEndpoints.Map(app);
        GraphPageEndpoints.Map(app);
        GraphStatsEndpoint.Map(app);
        MemoryEndpoints.Map(app);
        ToolCatalog.Map(app);
        SearchEndpoint.Map(app);
        ContextEndpoint.Map(app);
        TraceEndpoint.Map(app);
        InheritorsEndpoint.Map(app);
        ImpactEndpoint.Map(app);
    }

    public static IResult Failure(IEnumerable<IError> errors, int defaultStatus = 400)
    {
        var all = errors.ToArray();
        var status = all.OfType<ServiceError>().Select(error => (int)error.ErrorCode).FirstOrDefault(defaultStatus);
        return Results.Problem(statusCode: status, title: "Request failed", detail: string.Join("; ", all.Select(error => error.Message)));
    }

    public static IResult Invalid(string detail) => Results.Problem(statusCode: 400, title: "Invalid request", detail: detail);
    public static IResult MissingNode(int nodeId) => Results.Problem(statusCode: 404, title: "Node not found", detail: $"No indexed code node exists for id {nodeId}.");
}
