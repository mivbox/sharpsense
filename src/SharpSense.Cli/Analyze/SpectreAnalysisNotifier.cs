using System.Diagnostics;
using SharpSense.Application.Indexing.Notifications;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SharpSense.Cli.Analyze;

/// <summary>Workers only update bounded shared snapshots. A single presenter owns terminal rendering.</summary>
internal sealed class SpectreAnalysisNotifier(IAnsiConsole console, string workspaceName) : IAnalysisNotifier
{
    private readonly AnalysisSnapshotStore _snapshots = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private string _state = "Preparing analysis...";
    private string? _lastMilestone;
    private Guid? _lastSummary;

    public void Notify(AnalysisNotification notification) => _snapshots.Notify(notification);

    public void SetState(string state) => Volatile.Write(ref _state, state);

    public async Task Run(Func<Task> action)
    {
        var interactive = console.Profile.Capabilities.Interactive && console.Profile.Capabilities.Ansi;
        if (interactive)
        {
            try
            {
                await console.Live(Render()).AutoClear(true).StartAsync(async context =>
                {
                    var operation = Task.Run(action);
                    try
                    {
                        while (!operation.IsCompleted)
                        {
                            context.UpdateTarget(Render());
                            await Task.WhenAny(operation, Task.Delay(120));
                        }
                        await operation;
                    }
                    finally
                    {
                        context.UpdateTarget(Render());
                    }
                });
            }
            finally
            {
                WriteMilestone(final: true);
            }
        }
        else
        {
            console.WriteLine($"Workspace: {workspaceName}");
            var operation = Task.Run(action);
            try
            {
                while (!operation.IsCompleted)
                {
                    WriteMilestone(final: false);
                    await Task.WhenAny(operation, Task.Delay(150));
                }
                await operation;
            }
            finally
            {
                WriteMilestone(final: true);
            }
        }
    }

    private IRenderable Render()
    {
        var snapshot = _snapshots.Snapshot;
        var status = Volatile.Read(ref _state);
        if (snapshot?.State == "running" && snapshot.Phase is { } phase)
        {
            status += $" · {phase}";
        }
        var table = new Table().Expand().AddColumn("Language").AddColumn("Source").AddColumn("Activity");
        if (snapshot is not null)
        {
            foreach (var source in snapshot.Sources)
            {
                var activity = source.State == "running" ? source.Message ?? "Working..." : source.State;
                if (source.TotalItems is > 0)
                {
                    activity += $" ({source.CompletedItems ?? 0}/{source.TotalItems})";
                }
                table.AddRow(source.Kind.ToString(), Markup.Escape(source.Path), Markup.Escape(activity));
            }
        }
        var rows = new List<IRenderable>
        {
            new Markup($"[bold]{Markup.Escape(status)}[/]  [grey]{_elapsed.Elapsed:hh\\:mm\\:ss}[/]"),
            new Text(snapshot?.Message ?? "Waiting for analysis progress..."),
            table
        };
        if (snapshot?.TotalItems is > 0)
        {
            rows.Add(new Text($"{snapshot.CompletedItems ?? 0}/{snapshot.TotalItems} items"));
        }
        if (snapshot?.LastCommittedSummary is { } summary)
        {
            rows.Add(new Text(DescribeSummary(summary)));
        }
        if (snapshot?.Diagnostics.Count > 0)
        {
            rows.Add(new Text(string.Join(Environment.NewLine, snapshot.Diagnostics.Take(3))));
        }
        return new Panel(new Rows(rows)).Header(Markup.Escape($"SharpSense · {workspaceName}"));
    }

    private void WriteMilestone(bool final)
    {
        var snapshot = _snapshots.Snapshot;
        var message = snapshot?.State == "running"
            ? $"{Volatile.Read(ref _state)} {snapshot.Phase}"
            : Volatile.Read(ref _state);
        if (message != _lastMilestone)
        {
            console.WriteLine(message);
            _lastMilestone = message;
        }
        if (snapshot?.State == "completed" && snapshot.Summary is { } summary && _lastSummary != snapshot.OperationId)
        {
            console.WriteLine(DescribeSummary(summary));
            _lastSummary = snapshot.OperationId;
        }
        if (final && snapshot is not null)
        {
            foreach (var diagnostic in snapshot.Diagnostics)
            {
                console.WriteLine($"Diagnostic: {diagnostic}");
            }
        }
    }

    private static string DescribeSummary(AnalysisSummary summary) =>
        $"Saved {summary.Nodes:N0} nodes, {summary.Edges:N0} edges, {summary.Documents:N0} documents. " +
        $"Sources: {summary.ExtractedSources} analyzed, {summary.ReusedSources} reused. " +
        $"Embeddings: {summary.GeneratedEmbeddings} generated, {summary.ReusedEmbeddings} reused. " +
        $"Diagnostics: {summary.DiagnosticCount}.";
}
