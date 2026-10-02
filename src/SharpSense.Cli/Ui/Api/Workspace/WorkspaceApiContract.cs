using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SharpSense.Cli.Ui.Api;

/// <summary>
/// Shares workspace selection rules between request enforcement and the generated API contract.
/// </summary>
internal static class WorkspaceApiContract
{
    public const string HeaderName = "X-SharpSense-Workspace";

    public static bool IsWorkspaceScopedPath(PathString path) =>
        path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/api/workspaces") &&
        !MatchesRoute(path, "/api/tools");

    public static bool MatchesRoute(PathString path, string route) =>
        string.Equals(path.Value?.TrimEnd('/'), route, StringComparison.OrdinalIgnoreCase);

    public static Task DescribeWorkspaceHeader(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken ct)
    {
        var path = new PathString("/" + context.Description.RelativePath?.TrimStart('/'));
        if (IsWorkspaceScopedPath(path))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = HeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Stable ID of the registered workspace used for this request. Obtain it from the workspace catalog.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "uuid"
                }
            });
        }

        return Task.CompletedTask;
    }
}
