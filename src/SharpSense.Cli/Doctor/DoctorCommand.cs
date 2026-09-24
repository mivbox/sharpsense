using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpSense.Application.GraphStats;
using SharpSense.Application.GraphStats.GetGraphStats;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Doctor;

[UsedImplicitly]
internal sealed class DoctorCommand : AbstractAsyncCommand<DoctorCommand.Settings>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public sealed class Settings : GlobalSettings
    {

        [CommandOption("--json")]
        public bool Json { get; init; }
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddGraphStats().AddGraphStatsInfrastructure();
        services.AddEmbeddingsInfrastructure();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>>();
        var stats = await handler.Handle(new GetGraphStatsQuery(), ct);
        var workspace = host.Services.GetRequiredService<WorkspaceSelection>();
        var options = host.Services.GetRequiredService<IOptions<LocalEmbeddingsOptions>>().Value;
        var checks = DoctorChecks.Run(workspace, options, ct);
        var hasErrors = checks.Any(check => check.Severity == "error") ||
            stats.Diagnostics.Any(diagnostic => diagnostic.Severity == "error") ||
            stats.LastAttempt?.Outcome is "failed" or "cancelled";
        var hasWarnings = checks.Concat(stats.Diagnostics).Concat(stats.LastAttempt?.Diagnostics ?? [])
            .Any(diagnostic => diagnostic.Severity == "warning");
        var report = new DoctorReport(hasErrors ? "attention-required" : hasWarnings ? "warning" : "ok",
            workspace.Definition.Name, workspace.Definition.Id, checks, stats);

        CommandOutput.Write(context, settings.Json
            ? JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine
            : Format(report));

        return hasErrors ? 1 : 0;
    }

    private static string Format(DoctorReport report)
    {
        var output = new StringBuilder();
        output.AppendLine($"SharpSense doctor: {report.Status}");
        output.AppendLine($"Workspace: {report.WorkspaceName} ({report.WorkspaceId})");
        output.AppendLine($"Repository: {report.Graph.RepositoryRoot}");
        output.AppendLine($"Database: {report.Graph.DatabasePath} ({report.Graph.DatabaseState})");

        foreach (var check in report.Checks.Concat(report.Graph.Diagnostics))
        {
            output.AppendLine($"[{check.Severity}] {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Suggestion))
            {
                output.AppendLine($"  {check.Suggestion}");
            }
        }

        output.AppendLine($"Graph: {report.Graph.CodeNodeCount} symbols, {report.Graph.FileCount} files, {report.Graph.EdgeCount} relationships, {report.Graph.MemoryCount} memories.");
        output.AppendLine($"Embeddings: {report.Graph.EmbeddedNodeCount}/{report.Graph.CodeNodeCount} symbols.");
        output.AppendLine(report.Graph.LastSuccessfulIndex is { } success
            ? $"Last successful index: {success.CompletedAt:O} ({success.DurationMs:N0} ms)."
            : "Last successful index: not recorded.");

        if (report.Graph.LastAttempt is { } attempt)
        {
            output.AppendLine($"Last attempt: {attempt.Outcome} ({attempt.Kind}, {attempt.DurationMs:N0} ms).");
            foreach (var diagnostic in attempt.Diagnostics ?? [])
            {
                output.AppendLine($"[{diagnostic.Severity}] {diagnostic.Message}");
                if (!string.IsNullOrWhiteSpace(diagnostic.Suggestion))
                {
                    output.AppendLine($"  {diagnostic.Suggestion}");
                }
            }
        }

        return output.ToString();
    }

    private sealed record DoctorReport(
        string Status,
        string WorkspaceName,
        Guid WorkspaceId,
        IReadOnlyList<IndexDiagnostic> Checks,
        GraphStatsSnapshot Graph);
}
