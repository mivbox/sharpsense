using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Refactoring;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.Refactoring;

public sealed class RoslynWorkspaceRefactorerTests
{
    [Fact]
    public async Task WhenTargetPathIsOmittedAndSingleProjectExists_ThenItWritesTheUpdatedNodeToDisk()
    {
        using var fixture = TemporaryRepository.Create();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWorkspaceRefactorer>();
        var workspaceLoader = scope.ServiceProvider.GetRequiredService<IWorkspaceLoader>();
        var replacementCode =
            "    public string Updated()\n" +
            "    {\n" +
            "        return \"updated\";\n" +
            "    }\n";

        var result = await writer.RefactorNode(
            new RefactorTarget(
                1,
                "Feature.cs",
                5,
                8),
            replacementCode,
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ModifiedFilePaths.Should().Equal("Feature.cs");
        var updatedFileContents = await File.ReadAllTextAsync(
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        updatedFileContents.ReplaceLineEndings("\n").Should().Be(
            """
            namespace Fixture;

            public sealed class Feature
            {
                public string Updated()
                {
                    return "updated";
                }
            }
            """.ReplaceLineEndings("\n"));
        var loadedWorkspace = await workspaceLoader.Load(
            fixture.ProjectFilePath,
            ct: TestContext.Current.CancellationToken);
        var loadedDocumentContents = await ReadDocumentContents(
            loadedWorkspace.Solution,
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        loadedDocumentContents.ReplaceLineEndings("\n").Should().Be(
            """
            namespace Fixture;

            public sealed class Feature
            {
                public string Updated()
                {
                    return "updated";
                }
            }
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task WhenPersistedSpanFallsOutsideTheDocument_ThenItReturnsFailureWithoutChangingTheFile()
    {
        using var fixture = TemporaryRepository.Create();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWorkspaceRefactorer>();

        var result = await writer.RefactorNode(
            new RefactorTarget(
                1,
                "Feature.cs",
                50,
                60),
            "public string Updated() { return \"updated\"; }",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("falls outside");
        var fileContents = await File.ReadAllTextAsync(
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        fileContents.Should().Contain("Original()");
        fileContents.Should().NotContain("Updated()");
    }

    [Fact]
    public async Task WhenMultipleProjectTargetsExist_ThenItRequiresAnExplicitTarget()
    {
        using var fixture = TemporaryRepository.CreateWithMultipleProjects();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWorkspaceRefactorer>();

        var result = await writer.RefactorNode(
            new RefactorTarget(
                1,
                "Feature.cs",
                1,
                1),
            "public string Updated() { return \"updated\"; }",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Multiple project targets");
    }

    private static ServiceProvider CreateServiceProvider(
        string workingDirectory,
        IFileSystem fileSystem)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        services.AddRepositoryWorkspace(workingDirectory);
        services.AddIndexingInfrastructure();
        services.AddRefactoringInfrastructure();
        return services.BuildServiceProvider();
    }

    private static async Task<string> ReadDocumentContents(
        Solution solution,
        string sourceFilePath,
        CancellationToken ct)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .Single(candidate => candidate.FilePath is not null &&
                                 pathComparer.Equals(Path.GetFullPath(candidate.FilePath), sourceFilePath));

        return (await document.GetTextAsync(ct)).ToString();
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(
            string rootPath,
            string projectFilePath,
            string sourceFilePath)
        {
            RootPath = rootPath;
            ProjectFilePath = projectFilePath;
            SourceFilePath = sourceFilePath;
        }

        public string RootPath { get; }

        public string ProjectFilePath { get; }

        public string SourceFilePath { get; }

        public static TemporaryRepository Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "sharpsense-refactor-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, ".git"));
            File.WriteAllText(Path.Combine(rootPath, ".git", "HEAD"), "ref: refs/heads/main");

            var projectFilePath = Path.Combine(rootPath, "Fixture.csproj");
            var sourceFilePath = Path.Combine(rootPath, "Feature.cs");
            File.WriteAllText(
                projectFilePath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                sourceFilePath,
                """
                namespace Fixture;

                public sealed class Feature
                {
                    public string Original()
                    {
                        return "original";
                    }
                }
                """);

            return new TemporaryRepository(
                rootPath,
                projectFilePath,
                sourceFilePath);
        }

        public static TemporaryRepository CreateWithMultipleProjects()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "sharpsense-refactor-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, ".git"));
            File.WriteAllText(Path.Combine(rootPath, ".git", "HEAD"), "ref: refs/heads/main");
            File.WriteAllText(
                Path.Combine(rootPath, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(rootPath, "Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            return new TemporaryRepository(
                rootPath,
                Path.Combine(rootPath, "App.csproj"),
                Path.Combine(rootPath, "Feature.cs"));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
