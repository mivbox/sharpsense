using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class MsBuildWorkspaceFactoryTests
{
    [Fact]
    public async Task WhenProjectPromotesAuditWarning_ThenAnalysisKeepsWarningWithoutChangingProject()
    {
        using var fixture = new WarningProject("""
            <Warning Code="NU1904" Text="Synthetic critical audit warning." />
            """);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());
        var projectBefore = File.ReadAllText(fixture.ProjectPath);

        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains(result.Value.Diagnostics, diagnostic =>
            diagnostic.Contains("MSBuild warning NU1904", StringComparison.Ordinal) &&
            diagnostic.Contains("Synthetic critical audit warning", StringComparison.Ordinal));
        Assert.Contains(result.Value.Solution.Projects.SelectMany(project => project.Documents),
            document => document.Name == "Feature.cs");
        Assert.Equal(projectBefore, File.ReadAllText(fixture.ProjectPath));
    }

    [Theory]
    [InlineData("Warning", true)]
    [InlineData("Error", false)]
    public async Task WhenAssetsReplayAuditDiagnostic_ThenOnlyWarningPromotionIsRelaxed(string level, bool succeeds)
    {
        using var fixture = new WarningProject();
        await fixture.Restore(TestContext.Current.CancellationToken);
        var assets = JsonNode.Parse(File.ReadAllText(fixture.AssetsPath))!.AsObject();
        assets["logs"] = new JsonArray(new JsonObject
        {
            ["code"] = "NU1904",
            ["level"] = level,
            ["message"] = "Synthetic cached critical audit diagnostic.",
            ["libraryId"] = "Fixture.Vulnerable",
            ["targetGraphs"] = new JsonArray("net10.0")
        });
        File.WriteAllText(fixture.AssetsPath, assets.ToJsonString());
        var assetsBefore = File.ReadAllText(fixture.AssetsPath);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);

        Assert.True(succeeds == result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        var diagnostics = result.IsSuccess
            ? result.Value.Diagnostics
            : result.Errors.Select(error => error.Message);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Contains("NU1904", StringComparison.Ordinal) &&
            diagnostic.Contains("Synthetic cached critical audit diagnostic", StringComparison.Ordinal));
        Assert.Equal(assetsBefore, File.ReadAllText(fixture.AssetsPath));
    }

    [Fact]
    public async Task WhenProjectReportsGenuineError_ThenAnalysisStillFails()
    {
        using var fixture = new WarningProject("""
            <Error Code="SSFIXTURE001" Text="Required project input is unavailable." />
            """);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message.Contains("Required project input is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenWarningAndErrorHaveSameMessage_ThenWarningCannotHideFailure()
    {
        using var fixture = new WarningProject("""
            <Warning Code="NU1904" Text="Shared diagnostic message." />
            <Error Code="SSFIXTURE002" Text="Shared diagnostic message." />
            """);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message.Contains("SSFIXTURE002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenLoadIsCancelled_ThenCaptureCleanupPreservesCancellation()
    {
        using var fixture = new WarningProject();
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            loader.Load(fixture.ProjectPath, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void DiagnosticCaptureUsesPrivateDirectoryAndDeletesItOnDispose()
    {
        using var workspace = new MsBuildWorkspaceFactory().Create();
        string directory;
        MsBuildDiagnosticLog log;
        using (log = new MsBuildDiagnosticLog())
        {
            directory = log.DirectoryPath;
            Assert.True(Directory.Exists(directory));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(directory));
            }
            File.WriteAllText(Path.Combine(directory, "temporary.binlog"), "fixture");
        }

        log.Dispose();
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task WhenCSharpContainsCompilerError_ThenResolvableSymbolsAndCallsRemainExtractable()
    {
        using var fixture = new WarningProject();
        await fixture.Restore(TestContext.Current.CancellationToken);
        File.WriteAllText(fixture.SourcePath, """
            namespace Fixture;
            public class Feature
            {
                public int Broken() => MissingName;
                public void Caller() => Known();
                public void Known() { }
            }
            """);
        var fileSystem = new FileSystem();
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        var project = Assert.Single(result.Value.Solution.Projects);
        var compilation = await project.GetCompilationAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(compilation);
        Assert.Contains(compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic => diagnostic.Id == "CS0103" && diagnostic.Severity == DiagnosticSeverity.Error);
        var workspace = new RepositoryWorkspace(fixture.RootPath, Path.Combine(fixture.RootPath, "unused.db"), fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);

        var graph = await engine.Extract(fixture.ProjectPath, result.Value.Solution, workspace,
            ct: TestContext.Current.CancellationToken);

        var caller = Assert.Single(graph.CodeNodes, node => node.FullyQualifiedName == "Fixture.Feature.Caller()");
        var callee = Assert.Single(graph.CodeNodes, node => node.FullyQualifiedName == "Fixture.Feature.Known()");
        Assert.Contains(graph.Edges, edge => edge.CallerId == caller.CanonicalId &&
            edge.CalleeId == callee.CanonicalId && edge.EdgeType == EdgeType.MethodCall);
    }

    private sealed class WarningProject : IDisposable
    {
        public WarningProject(string tasks = "")
        {
            RootPath = Path.Combine(Path.GetTempPath(), "sharpsense-warning-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            File.WriteAllText(ProjectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <WarningsAsErrors>NU1904</WarningsAsErrors>
                    <MSBuildWarningsAsErrors>NU1904</MSBuildWarningsAsErrors>
                    <NuGetAudit>true</NuGetAudit>
                  </PropertyGroup>
                  <Target Name="VerifyAnalysisWarnings" BeforeTargets="CoreCompile">
                    <Error Condition="'$(NuGetAudit)' == 'false'" Code="SSAUDITDISABLED" Text="Audit must remain enabled." />
                    {{tasks}}
                  </Target>
                </Project>
                """);
            File.WriteAllText(SourcePath, "namespace Fixture; public class Feature { public void Known() { } }");
            File.WriteAllText(Path.Combine(RootPath, "NuGet.Config"), """
                <configuration><packageSources><clear /></packageSources></configuration>
                """);
        }

        public string RootPath { get; }
        public string ProjectPath => Path.Combine(RootPath, "Fixture.csproj");
        public string SourcePath => Path.Combine(RootPath, "Feature.cs");
        public string AssetsPath => Path.Combine(RootPath, "obj", "project.assets.json");

        public async Task Restore(CancellationToken ct)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("restore");
            start.ArgumentList.Add(ProjectPath);
            start.ArgumentList.Add("--configfile");
            start.ArgumentList.Add(Path.Combine(RootPath, "NuGet.Config"));
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                throw;
            }

            Assert.True(process.ExitCode == 0, await output + await error);
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
