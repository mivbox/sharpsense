using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.Json.Nodes;

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

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        result.Value.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Contains("MSBuild warning NU1904", StringComparison.Ordinal) &&
            diagnostic.Contains("Synthetic critical audit warning", StringComparison.Ordinal));
        result.Value.Solution.Projects
            .SelectMany(project => project.Documents).Should().Contain(document => document.Name == "Feature.cs");
        File.ReadAllText(fixture.ProjectPath).Should().Be(projectBefore);
    }

    [Theory]
    [InlineData("Warning", true)]
    [InlineData("Error", false)]
    public async Task WhenAssetsReplayAuditDiagnostic_ThenOnlyWarningPromotionIsRelaxed(string level, bool succeeds)
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new WarningProject();
        await fixture.Restore(ct);
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

        var result = await loader.Load(fixture.ProjectPath, ct);

        (succeeds == result.IsSuccess).Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        var diagnostics = result.IsSuccess
            ? result.Value.Diagnostics
            : result.Errors.Select(error => error.Message);
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Contains("NU1904", StringComparison.Ordinal) &&
            diagnostic.Contains("Synthetic cached critical audit diagnostic", StringComparison.Ordinal));
        File.ReadAllText(fixture.AssetsPath).Should().Be(assetsBefore);
    }

    [Fact]
    public async Task WhenProjectReportsGenuineError_ThenAnalysisStillFails()
    {
        using var fixture = new WarningProject("""
            <Error Code="SSFIXTURE001" Text="Required project input is unavailable." />
            """);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        var result = await loader.Load(fixture.ProjectPath, TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message.Contains(
            "Required project input is unavailable",
            StringComparison.Ordinal));
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

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message.Contains("SSFIXTURE002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenLoadIsCancelled_ThenCaptureCleanupPreservesCancellation()
    {
        using var fixture = new WarningProject();
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), new FileSystem());

        await ((Func<Task>)(() =>
            loader.Load(
                fixture.ProjectPath,
                new CancellationToken(canceled: true)))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WhenDiagnosticCapture_ThenUsesPrivateDirectoryAndDeletesItOnDispose()
    {
        using var workspace = new MsBuildWorkspaceFactory().Create();
        string directory;
        MsBuildDiagnosticLog log;
        using (log = new MsBuildDiagnosticLog())
        {
            directory = log.DirectoryPath;
            Directory.Exists(directory).Should().BeTrue();
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(directory).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            File.WriteAllText(Path.Combine(directory, "temporary.binlog"), "fixture");
        }

        log.Dispose();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public async Task WhenCSharpContainsCompilerError_ThenResolvableSymbolsAndCallsRemainExtractable()
    {
        using var fixture = new WarningProject();
        await fixture.Restore(TestContext.Current.CancellationToken);
        File.WriteAllText(
            fixture.SourcePath,
            """
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
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        var project = result.Value.Solution.Projects.Should().ContainSingle().Which;
        var compilation = await project.GetCompilationAsync(TestContext.Current.CancellationToken);
        compilation.Should().NotBeNull();
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().Contain(diagnostic => diagnostic.Id == "CS0103" && diagnostic.Severity == DiagnosticSeverity.Error);
        var workspace = new RepositoryWorkspace(
            fixture.RootPath,
            Path.Combine(fixture.RootPath, "unused.db"),
            fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);

        var graph = await engine.Extract(
            fixture.ProjectPath,
            result.Value.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);

        var caller = graph.CodeNodes.Should().ContainSingle(node => node.FullyQualifiedName == "Fixture.Feature.Caller()").Which;
        var callee = graph.CodeNodes.Should().ContainSingle(node => node.FullyQualifiedName == "Fixture.Feature.Known()").Which;
        graph.Edges.Should().Contain(edge => edge.CallerId == caller.CanonicalId &&
            edge.CalleeId == callee.CanonicalId && edge.EdgeType == EdgeType.MethodCall);
    }

    [Fact]
    public async Task WhenReferencedProjectHasBuildOutput_ThenPreservesSourceRelationships()
    {
        using var fixture = new WarningProject();
        var ct = TestContext.Current.CancellationToken;
        var dependencyDirectory = Path.Combine(fixture.RootPath, "Dependency");
        Directory.CreateDirectory(dependencyDirectory);
        File.WriteAllText(
            Path.Combine(dependencyDirectory, "Dependency.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(dependencyDirectory, "Target.cs"),
            """
            namespace Dependency;
            public class Target { public int Get() => 1; }
            """);
        File.WriteAllText(
            fixture.SourcePath,
            """
            namespace Fixture;
            public class Feature { public int Run() => new Dependency.Target().Get(); }
            """);
        File.WriteAllText(
            fixture.ProjectPath,
            File.ReadAllText(fixture.ProjectPath)
                .Replace(
                    "</Project>",
                    """
              <ItemGroup>
                <Compile Remove="Dependency/**/*.cs" />
                <ProjectReference Include="Dependency/Dependency.csproj" />
              </ItemGroup>
            </Project>
            """));
        await fixture.Restore(ct);
        var fileSystem = new FileSystem();
        var repository = new RepositoryWorkspace(
            fixture.RootPath,
            Path.Combine(fixture.RootPath, "unused.db"),
            fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);

        async Task<KnowledgeGraphExtractionPayload> Extract()
        {
            using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
            var result = await loader.Load(fixture.ProjectPath, ct);
            result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));

            return await engine.Extract(fixture.ProjectPath, result.Value.Solution, repository, ct: ct);
        }

        var before = await Extract();
        await fixture.Build(ct);
        var after = await Extract();

        var caller = before.CodeNodes.Should().ContainSingle(node => node.FullyQualifiedName == "Fixture.Feature.Run()").Which;
        var callee = before.CodeNodes.Should().ContainSingle(node => node.FullyQualifiedName == "Dependency.Target.Get()").Which;
        before.Edges.Should().Contain(edge => edge.CallerId == caller.CanonicalId &&
            edge.CalleeId == callee.CanonicalId && edge.EdgeType == EdgeType.MethodCall);
        after.CodeNodes.Should().Contain(node => node.CanonicalId == callee.CanonicalId);
        after.Edges.Should().BeEquivalentTo(before.Edges);
    }

    [Fact]
    public async Task WhenProjectsTargetMultipleFrameworks_ThenPersistsDistinctSymbolsAndMatchingReferences()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new WarningProject();
        const string projectXml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>
                <LangVersion>latest</LangVersion>
              </PropertyGroup>
            </Project>
            """;
        var dependencyDirectory = Path.Combine(fixture.RootPath, "Dependency");
        Directory.CreateDirectory(dependencyDirectory);
        await File.WriteAllTextAsync(Path.Combine(dependencyDirectory, "Dependency.csproj"), projectXml, ct);
        await File.WriteAllTextAsync(
            Path.Combine(dependencyDirectory, "Target.cs"),
            """
            namespace Dependency;
            public static class Target
            {
                public static int Read() => 1;
            #if NET10_0
                public static int Modern() => 2;
            #endif
            }
            """,
            ct);
        await File.WriteAllTextAsync(
            fixture.ProjectPath,
            projectXml.Replace("</Project>", """
              <ItemGroup>
                <Compile Remove="Dependency/**/*.cs" />
                <ProjectReference Include="Dependency/Dependency.csproj" />
              </ItemGroup>
            </Project>
            """),
            ct);
        await File.WriteAllTextAsync(
            fixture.SourcePath,
            """
            namespace Fixture;
            public static class Feature
            {
                public static int Run() => Dependency.Target.Read();
            #if NET10_0
                public static int Modern() => Dependency.Target.Modern();
            #endif
            }
            """,
            ct);
        await fixture.Restore(ct);
        var fileSystem = new FileSystem();
        var workspace = new RepositoryWorkspace(fixture.RootPath, Path.Combine(fixture.RootPath, "unused.db"), fileSystem);
        using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
        var extractor = new CSharpLanguageExtractor(
            loader,
            engine,
            workspace,
            new CSharpWorkspaceTargetResolver(workspace, fileSystem));

        var result = await extractor.Extract(new ExtractionContext(fixture.ProjectPath, null), ct);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        var graph = result.Value;
        graph.Projects.Should().HaveCount(4);
        graph.Projects.Select(project => project.Id).Should().OnlyHaveUniqueItems();
        foreach (var framework in new[] { "net10.0", "netstandard2.1" })
        {
            var project = graph.Projects.Single(project => project.Name == $"Fixture({framework})");
            var dependency = graph.Projects.Single(project => project.Name == $"Dependency({framework})");
            var caller = graph.CodeNodes.Single(node => node.ProjectId == project.Id && node.FullyQualifiedName == "Fixture.Feature.Run()");
            var callee = graph.CodeNodes.Single(node => node.ProjectId == dependency.Id && node.FullyQualifiedName == "Dependency.Target.Read()");
            graph.Edges.Should().Contain(edge => edge.CallerId == project.Id &&
                edge.CalleeId == dependency.Id && edge.EdgeType == EdgeType.ProjectReference);
            graph.Edges.Should().Contain(edge => edge.CallerId == caller.CanonicalId &&
                edge.CalleeId == callee.CanonicalId && edge.EdgeType == EdgeType.MethodCall);
            graph.Edges.Should().NotContain(edge => edge.CallerId == caller.CanonicalId &&
                edge.CalleeId != callee.CanonicalId && edge.EdgeType == EdgeType.MethodCall);
            graph.CodeNodes.Count(node => node.ProjectId == project.Id && node.FullyQualifiedName == "Fixture.Feature.Modern()")
                .Should().Be(framework == "net10.0" ? 1 : 0);
        }

        await using var database = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(database.CreateDbContextFactory());
        await repository.ReplaceWorkspace(graph, ct);
        await using var context = await database.GetContext(ct);
        (await context.ProjectNodes.CountAsync(ct)).Should().Be(4);
        var persisted = await repository.GetPersistedCodeNodes(ct);
        persisted.Select(node => node.CanonicalId).Should().BeEquivalentTo(graph.CodeNodes.Select(node => node.CanonicalId));

        var reloaded = await extractor.Extract(new ExtractionContext(fixture.ProjectPath, null), ct);
        reloaded.IsSuccess.Should().BeTrue(string.Join("; ", reloaded.Errors.Select(error => error.Message)));
        reloaded.Value.Projects.Select(project => project.Id).Should().BeEquivalentTo(graph.Projects.Select(project => project.Id));
        reloaded.Value.Edges.Should().BeEquivalentTo(graph.Edges);
    }

    private sealed class WarningProject : IDisposable
    {
        public WarningProject(string tasks = "")
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "sharpsense-warning-tests",
                Guid.NewGuid()
                    .ToString("N"));
            Directory.CreateDirectory(RootPath);
            File.WriteAllText(
                ProjectPath,
                $$"""
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
            File.WriteAllText(
                Path.Combine(RootPath, "NuGet.Config"),
                """
                <configuration><packageSources><clear /></packageSources></configuration>
                """);
        }

        public string RootPath { get; }
        public string ProjectPath => Path.Combine(RootPath, "Fixture.csproj");
        public string SourcePath => Path.Combine(RootPath, "Feature.cs");
        public string AssetsPath => Path.Combine(RootPath, "obj", "project.assets.json");

        public Task Restore(CancellationToken ct)
            => RunDotnet(["restore", ProjectPath, "--configfile", Path.Combine(RootPath, "NuGet.Config")], ct);

        public Task Build(CancellationToken ct)
            => RunDotnet(["build", ProjectPath, "--no-restore"], ct);

        private async Task RunDotnet(string[] arguments, CancellationToken ct)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
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

            (process.ExitCode == 0).Should().BeTrue(await output + await error);
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
