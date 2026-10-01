using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class MemoryEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/memory")
            .WithTags("Memory");

        group.MapGetNodeMemoriesEndpoint()
            .MapGetMemoryEndpoint()
            .MapGetMemoriesEndpoint()
            .MapAddMemoryEndpoint()
            .MapDeleteMemoryEndpoint();

        return builder;
    }
}
