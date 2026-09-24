using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using SharpSense.Cli.Ui.Indexing;

namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceSummary(Guid Id, string Name, string RepositoryRoot, IReadOnlyList<WorkspaceSourceOverview> Sources);

public sealed record WorkspaceCatalogResponse(IReadOnlyList<WorkspaceSummary> Workspaces, Guid? InitialWorkspaceId);

public sealed record CreateWorkspaceRequest(string Name, string RepositoryRoot, WorkspaceSource[] Sources);

public sealed record UpdateWorkspaceRequest(string Name, WorkspaceSource[] Sources);

public sealed record MergeWorkspacesRequest(string Name, Guid[] WorkspaceIds);

public sealed record DiscoverWorkspaceRequest(string RepositoryRoot);

public sealed record WorkspaceDiscoveryResponse(string RepositoryRoot, IReadOnlyList<WorkspaceSourceOverview> Sources);

internal static class WorkspaceCatalogEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/workspaces", (WorkspaceCatalog catalog, WorkspaceUiOptions options) =>
        {
            var selections = catalog.List();
            Guid? initialId = null;
            if (!string.IsNullOrWhiteSpace(options.InitialWorkspace))
            {
                initialId = selections.FirstOrDefault(selection =>
                    string.Equals(selection.Definition.Name, options.InitialWorkspace, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(selection.Definition.Id.ToString(), options.InitialWorkspace, StringComparison.OrdinalIgnoreCase))?.Definition.Id;
            }

            return new WorkspaceCatalogResponse(selections.Select(ToSummary).ToArray(), initialId);
        }).WithName("ListWorkspaces").WithTags("Workspaces").Produces<WorkspaceCatalogResponse>();

        app.MapGet("/api/workspaces/{workspaceId:guid}", (Guid workspaceId, WorkspaceCatalog catalog) =>
                ToSummary(Resolve(catalog, workspaceId)))
            .WithName("GetWorkspace").WithTags("Workspaces").Produces<WorkspaceSummary>().ProducesProblem(404);

        app.MapPost("/api/workspaces", (CreateWorkspaceRequest request, WorkspaceCatalog catalog) =>
        {
            var selected = catalog.Create(request.Name, request.RepositoryRoot, request.Sources);
            return Results.Created($"/api/workspaces/{selected.Definition.Id:D}", ToSummary(selected));
        }).WithName("CreateWorkspace").WithTags("Workspaces").Produces<WorkspaceSummary>(201).ProducesProblem(400);

        app.MapPut("/api/workspaces/{workspaceId:guid}", (Guid workspaceId, UpdateWorkspaceRequest request, WorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator) =>
        {
            Resolve(catalog, workspaceId);
            return coordinator.UpdateWhileIdle(workspaceId,
                () => ToSummary(catalog.Update(workspaceId.ToString(), request.Name, request.Sources)));
        }).WithName("UpdateWorkspace").WithTags("Workspaces").Produces<WorkspaceSummary>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        app.MapPost("/api/workspaces/merge", (MergeWorkspacesRequest request, WorkspaceCatalog catalog) =>
        {
            ArgumentNullException.ThrowIfNull(request.WorkspaceIds);
            var selection = catalog.Merge(request.Name, request.WorkspaceIds.Select(static id => id.ToString()));
            return Results.Created($"/api/workspaces/{selection.Definition.Id:D}", ToSummary(selection));
        }).WithName("MergeWorkspaces").WithTags("Workspaces").Produces<WorkspaceSummary>(201).ProducesProblem(400);

        app.MapPost("/api/workspaces/discover", (DiscoverWorkspaceRequest request, WorkspaceSourceDiscovery discovery, CancellationToken ct) =>
                discovery.Discover(request.RepositoryRoot, ct))
            .WithName("DiscoverWorkspaceSources").WithTags("Workspaces").Produces<WorkspaceDiscoveryResponse>().ProducesProblem(400);
    }

    internal static WorkspaceSelection Resolve(WorkspaceCatalog catalog, Guid id) =>
        catalog.ResolveById(id);

    private static WorkspaceSummary ToSummary(WorkspaceSelection selection) => new(
        selection.Definition.Id,
        selection.Definition.Name,
        selection.Definition.RepositoryRoot,
        selection.Definition.Sources.Select(static source => new WorkspaceSourceOverview(source.Kind.ToString(), source.Path)).ToArray());
}
