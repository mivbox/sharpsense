using AwesomeAssertions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Analyze;
using Spectre.Console.Testing;

namespace SharpSense.Cli.Tests;

public sealed class SpectreAnalysisNotifierTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenCommittedSummary_ThenIsRenderedToInjectedConsole(bool interactive)
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Capabilities.Ansi = interactive;
        var presenter = new SpectreAnalysisNotifier(console, "workspace [literal]");
        var id = Guid.NewGuid();
        await presenter.Run(async () =>
        {
            presenter.Notify(new(id, 1, DateTimeOffset.UtcNow, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
            presenter.Notify(new(
                id,
                2,
                DateTimeOffset.UtcNow,
                AnalysisNotificationKind.SourceStarted,
                AnalysisOperationKind.Full,
                AnalysisPhase.Extraction,
                new AnalysisSource(WorkspaceSourceKind.CSharp, "[source].csproj"),
                "Loading [literal] source",
                0,
                0));
            await Task.Delay(180, TestContext.Current.CancellationToken);
            presenter.Notify(new(
                id,
                3,
                DateTimeOffset.UtcNow,
                AnalysisNotificationKind.Committed,
                AnalysisOperationKind.Full,
                Summary: new AnalysisSummary(1, 10, 20, 3, 1, 2, 4, 5, 0)));
            presenter.SetState("Indexed workspace.");
        });
        console.Output.Should().Contain("Saved 10 nodes, 20 edges, 3 documents");
        console.Output.Should().Contain("1 analyzed, 2 reused");
        console.Output.Should().NotContain("100%");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenFailedRendering_ThenRetainsDiagnosticsAfterLiveDisplayClears(bool interactive)
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Capabilities.Ansi = interactive;
        var presenter = new SpectreAnalysisNotifier(console, "fixture");
        var id = Guid.NewGuid();
        await ((Func<Task>)(() => presenter.Run(async () =>
        {
            presenter.Notify(new(id, 1, DateTimeOffset.UtcNow, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
            for (var sequence = 2; sequence <= 25; sequence++)
            {
                presenter.Notify(new(
                    id,
                    sequence,
                    DateTimeOffset.UtcNow,
                    AnalysisNotificationKind.Diagnostic,
                    AnalysisOperationKind.Full,
                    Message: $"Warning {sequence}"));
            }

            presenter.Notify(new(
                id,
                26,
                DateTimeOffset.UtcNow,
                AnalysisNotificationKind.Failed,
                AnalysisOperationKind.Full,
                Message: "Actual [failure]"));
            await Task.Yield();
            throw new InvalidOperationException("Fixture failed");
        }))).Should().ThrowExactlyAsync<InvalidOperationException>();
        console.Output.Should().Contain("Diagnostic: Actual [failure]");
        console.Output.Should().NotContain("Saved ");
    }
}
