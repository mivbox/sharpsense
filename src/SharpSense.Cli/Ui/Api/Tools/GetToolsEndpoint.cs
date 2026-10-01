using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class GetToolsEndpoint
{
    public static IEndpointRouteBuilder MapGetToolsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/tools", GetTools)
            .WithName(nameof(GetTools))
            .WithTags("Tools")
            .Produces<ToolDescriptor[]>();

        return builder;
    }

    private static ToolDescriptor[] GetTools() => new ToolDescriptor[]
    {
        new(
            "graph_stats",
            "Graph statistics",
            "Inspect graph coverage, the last successful index, phase timings, and indexing diagnostics.",
            false),
        new("search", "Semantic search", "Find code and documentation using keyword and semantic matching.", false),
        new("context", "Node context", "Inspect immediate callers, callees, and hierarchy.", true),
        new("trace", "Trace dependencies", "Follow callers or callees across the indexed graph.", true),
        new("inheritors", "Find inheritors", "Find direct subclasses and interface implementations.", true),
        new("impact", "Impact analysis", "Explore upstream dependencies affected by a change.", true)
    };
}
