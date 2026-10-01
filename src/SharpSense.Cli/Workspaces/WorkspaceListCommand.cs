using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceListCommand : WorkspaceCatalogCommand<WorkspaceListCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var catalog = host.Services.GetRequiredService<IWorkspaceCatalog>();
        Guid? defaultWorkspaceId = null;
        try
        {
            defaultWorkspaceId = catalog.GetDefaultWorkspaceId();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                "Warning: Cannot read the saved default workspace. Run 'sharpsense workspace use <name-or-id>' to replace it.");
        }

        WorkspaceOutput.WriteList(context, catalog.List(), settings.Json, defaultWorkspaceId);

        return Task.FromResult(0);
    }
}
