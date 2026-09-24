using System.IO.Abstractions;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Ui.Api;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexLeaseIntegrationTests
{
    [Fact]
    public async Task SeparateCliProcessCannotBypassHeldWriterLease()
    {
        using var fixture = new Fixture();
        using var lease = fixture.Catalog.AcquireIndexLease(fixture.Selection);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     typeof(Cli.Program).Assembly.Location, "analyze", "--workspace",
                     fixture.Selection.Definition.Id.ToString(), "--no-embeddings"
                 })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["SHARPSENSE_HOME"] = fixture.HomeDirectory;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start fixture CLI.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;

            Assert.Equal(1, process.ExitCode);
            Assert.Contains("already being indexed or watched", output);
            Assert.False(File.Exists(fixture.Selection.Workspace.DatabasePath));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task CliWriterConflictFailsBeforeDatabaseInitialization()
    {
        using var fixture = new Fixture();
        using (fixture.Catalog.AcquireIndexLease(fixture.Selection))
        {
            var result = await RunAnalyze(fixture.Catalog, fixture.Selection);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("already being indexed or watched", result.Output);
            Assert.False(File.Exists(fixture.Selection.Workspace.DatabasePath));
        }

        var retry = await RunAnalyze(fixture.Catalog, fixture.Selection);
        Assert.Equal(0, retry.ExitCode);
        Assert.Equal(["docs/first.md"], await DocumentPaths(fixture.Selection));
    }

    [Fact]
    public async Task IndependentUiHostsCannotWriteSameWorkspaceUntilWatchStops()
    {
        using var fixture = new Fixture();
        var otherCatalog = new WorkspaceCatalog(new FileSystem(), fixture.HomeDirectory);
        await using var firstProvider = CreateUiServices(fixture.Catalog, fixture.RepositoryRoot);
        await using var secondProvider = CreateUiServices(otherCatalog, fixture.RepositoryRoot);
        var first = firstProvider.GetRequiredService<WorkspaceIndexingCoordinator>();
        var second = secondProvider.GetRequiredService<WorkspaceIndexingCoordinator>();
        first.Start(fixture.Selection, new StartWorkspaceIndexingRequest(Watch: true, SkipEmbeddings: true));
        await WaitForState(first, fixture.Selection, "watching");

        second.Start(otherCatalog.Resolve(fixture.Selection.Definition.Id.ToString(), fixture.RepositoryRoot),
            new StartWorkspaceIndexingRequest(SkipEmbeddings: true));
        var rejected = await WaitForState(second, fixture.Selection, "failed");
        Assert.Contains(rejected.Diagnostics, message => message.Contains("already being indexed or watched"));
        Assert.Equal(0, rejected.Revision);
        Assert.Equal(["docs/first.md"], await DocumentPaths(fixture.Selection));
        Assert.Throws<WorkspaceIndexBusyException>(() => otherCatalog.Update(
            fixture.Selection.Definition.Id.ToString(), "changed", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/second.md")]));

        await first.Stop(fixture.Selection.Definition.Id, TestContext.Current.CancellationToken);
        second.Start(otherCatalog.Resolve(fixture.Selection.Definition.Id.ToString(), fixture.RepositoryRoot),
            new StartWorkspaceIndexingRequest(SkipEmbeddings: true));
        await WaitForState(second, fixture.Selection, "completed");
        Assert.Equal(["docs/first.md"], await DocumentPaths(fixture.Selection));
    }

    [Fact]
    public async Task StaleSelectionsCannotReplaceExistingGraphFromCliOrUi()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, (await RunAnalyze(fixture.Catalog, fixture.Selection)).ExitCode);
        fixture.Catalog.Update(fixture.Selection.Definition.Id.ToString(), "workspace",
            [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/second.md")]);

        var staleCli = await RunAnalyze(fixture.Catalog, fixture.Selection);
        Assert.Equal(1, staleCli.ExitCode);
        Assert.Contains("changed after this command selected it", staleCli.Output);
        Assert.Equal(["docs/first.md"], await DocumentPaths(fixture.Selection));

        await using var provider = CreateUiServices(fixture.Catalog, fixture.RepositoryRoot);
        var coordinator = provider.GetRequiredService<WorkspaceIndexingCoordinator>();
        coordinator.Start(fixture.Selection, new StartWorkspaceIndexingRequest(SkipEmbeddings: true));
        var staleUi = await WaitForState(coordinator, fixture.Selection, "failed");
        Assert.Contains(staleUi.Diagnostics, message => message.Contains("changed after this command selected it"));
        Assert.Equal(["docs/first.md"], await DocumentPaths(fixture.Selection));
    }

    private static async Task<(int ExitCode, string Output)> RunAnalyze(WorkspaceCatalog catalog, WorkspaceSelection selection)
    {
        using var console = new TestConsole();
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var app = Cli.Program.CreateCommandApp(console, services =>
            {
                services.AddSingleton(catalog);
                services.AddSingleton(selection);
                services.AddSingleton(selection.Workspace);
            }, enableFileLogging: false);
            var exit = await app.RunAsync(
                ["analyze", "--workspace", selection.Definition.Name, "--repo-root", selection.Workspace.RootPath, "--no-embeddings"],
                TestContext.Current.CancellationToken);
            return (exit, console.Output);
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    private static ServiceProvider CreateUiServices(WorkspaceCatalog catalog, string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkspaceUiServices(new WorkspaceUiOptions(null, root, new Uri("http://localhost:50069")));
        services.AddSingleton(catalog);
        services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        services.AddIndexing();
        services.AddIndexingInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexRunRecording();
        services.AddWorkspaceIndexing();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<WorkspaceIndexingStatus> WaitForState(
        WorkspaceIndexingCoordinator coordinator,
        WorkspaceSelection selection,
        string expectedState)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var status = coordinator.GetStatus(selection.Definition.Id);
            if (status.State == expectedState)
            {
                return status;
            }

            Assert.True(status.State != "failed", string.Join(Environment.NewLine, status.Diagnostics));
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task<string[]> DocumentPaths(WorkspaceSelection selection)
    {
        await using var connection = new SqliteConnection($"Data Source={selection.Workspace.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RelativePath FROM Documents ORDER BY RelativePath";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var paths = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            paths.Add(reader.GetString(0));
        }

        return [.. paths];
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sharpsense-writer-lease-{Guid.NewGuid():N}");

        public Fixture()
        {
            RepositoryRoot = Path.Combine(_directory, "repo");
            HomeDirectory = Path.Combine(_directory, "home");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "docs"));
            File.WriteAllText(Path.Combine(RepositoryRoot, "docs", "first.md"), "# First workspace graph");
            File.WriteAllText(Path.Combine(RepositoryRoot, "docs", "second.md"), "# Replacement workspace graph");
            Catalog = new WorkspaceCatalog(new FileSystem(), HomeDirectory);
            Selection = Catalog.Create("workspace", RepositoryRoot, [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/first.md")]);
        }

        public string RepositoryRoot { get; }
        public string HomeDirectory { get; }
        public WorkspaceCatalog Catalog { get; }
        public WorkspaceSelection Selection { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
