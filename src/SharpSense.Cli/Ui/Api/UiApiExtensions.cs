using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class UiApiExtensions
{
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
            await Results.Problem(
                statusCode: status,
                title: status == 500 ? "Request failed" : "Invalid request",
                detail: status == 500 ? "An unexpected error occurred. Check the SharpSense server log." : exception?.Message)
                .ExecuteAsync(context);
        }));
        app.UseWorkspaceRequests();
        app.UseStatusCodePages(async status =>
        {
            if (status.HttpContext.Request.Path.StartsWithSegments("/api"))
            {
                await Results.Problem(statusCode: status.HttpContext.Response.StatusCode)
                    .ExecuteAsync(status.HttpContext);
            }
        });
        app.MapOpenApi();
        app.MapWorkspaceCatalogEndpoints();
        app.MapWorkspaceIndexingEndpoints();
        app.MapWorkspaceEndpoints();
        app.MapGraphEndpoints();
        app.MapMemoryEndpoints();
        app.MapToolsEndpoints();
    }
}
