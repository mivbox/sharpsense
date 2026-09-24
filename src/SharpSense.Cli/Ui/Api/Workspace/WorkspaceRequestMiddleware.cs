using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class WorkspaceRequestMiddleware
{
    public static void UseWorkspaceRequests(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<WorkspaceUiOptions>();
            var expected = options.Address;
            var request = context.Request;
            var hostMatches = string.Equals(request.Host.Host.Trim('[', ']'), expected.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) &&
                              (request.Host.Port ?? (request.IsHttps ? 443 : 80)) == expected.Port;
            var origin = request.Headers.Origin.ToString();
            var originMatches = string.IsNullOrEmpty(origin) ||
                                (Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
                                 string.Equals(originUri.GetLeftPart(UriPartial.Authority), expected.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
            if (!hostMatches || !originMatches || string.Equals(request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                await Results.Problem(statusCode: 403, title: "Request origin is not allowed").ExecuteAsync(context);
                return;
            }

            if (!WorkspaceApiContract.IsWorkspaceScopedPath(request.Path) || context.GetEndpoint() is null)
            {
                await next(context);
                return;
            }

            if (!Guid.TryParse(request.Headers[WorkspaceApiContract.HeaderName], out var workspaceId))
            {
                await Results.Problem(statusCode: 400, title: "Select a workspace",
                    detail: "Workspace requests require an X-SharpSense-Workspace header containing a workspace ID.").ExecuteAsync(context);
                return;
            }

            var catalog = context.RequestServices.GetRequiredService<WorkspaceCatalog>();
            var selection = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);
            context.RequestServices.GetRequiredService<WorkspaceScope>().Bind(selection);

            // Graph statistics remain usable for missing or incompatible databases.
            if (!request.Path.Equals(new PathString("/api/tools/graph-stats")))
            {
                await context.RequestServices.GetRequiredService<WorkspaceDatabaseInitializer>().InitializeAsync(context.RequestAborted);
            }

            await next(context);
        });
    }
}
