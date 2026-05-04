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

public sealed class WorkspaceRenamerTests
{
    [Fact]
    public async Task WhenSourceTargetIsRenamed_ThenItUpdatesDeclarationAndCallersOnDisk()
    {
        using var fixture = TemporaryRepository.Create();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var renamer = scope.ServiceProvider.GetRequiredService<IWorkspaceRenamer>();
        var workspaceLoader = scope.ServiceProvider.GetRequiredService<IWorkspaceLoader>();

        var result = await renamer.RenameSymbol(
            new NodeRefactorTarget(
                1,
                "Feature.cs",
                5,
                8,
                "Fixture.csproj",
                DocumentKind.Source),
            "Updated",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ModifiedFilePaths.Should().Equal("Feature.cs", "FeatureCaller.cs");
        var updatedSourceFileContents = await File.ReadAllTextAsync(
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        updatedSourceFileContents.ReplaceLineEndings("\n").Should().Be(
            """
            namespace Fixture;

            public sealed class Feature
            {
                public string Updated()
                {
                    return "original";
                }
            }
            """.ReplaceLineEndings("\n"));
        var updatedCallerFileContents = await File.ReadAllTextAsync(
            fixture.CallerFilePath,
            TestContext.Current.CancellationToken);
        updatedCallerFileContents.ReplaceLineEndings("\n").Should().Be(
            """
            namespace Fixture;

            public sealed class FeatureCaller
            {
                public string Call(Feature feature)
                {
                    return feature.Updated();
                }
            }
            """.ReplaceLineEndings("\n"));
        var loadedWorkspace = await workspaceLoader.Load(
            fixture.ProjectFilePath,
            ct: TestContext.Current.CancellationToken);
        var loadedSourceDocumentContents = await ReadDocumentContents(
            loadedWorkspace.Solution,
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        loadedSourceDocumentContents.ReplaceLineEndings("\n").Should().Contain("Updated()");
        var loadedCallerDocumentContents = await ReadDocumentContents(
            loadedWorkspace.Solution,
            fixture.CallerFilePath,
            TestContext.Current.CancellationToken);
        loadedCallerDocumentContents.ReplaceLineEndings("\n").Should().Contain("feature.Updated()");
    }

    [Fact]
    public async Task WhenPersistedProjectPathIsPresentAndMultipleProjectsExist_ThenItUsesTheOwningProjectWithoutExplicitTarget()
    {
        using var fixture = TemporaryRepository.CreateWithMultipleProjects();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var renamer = scope.ServiceProvider.GetRequiredService<IWorkspaceRenamer>();

        var result = await renamer.RenameSymbol(
            new NodeRefactorTarget(
                1,
                "Feature.cs",
                5,
                8,
                "App.csproj",
                DocumentKind.Source),
            "Updated",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ModifiedFilePaths.Should().Equal("Feature.cs", "FeatureCaller.cs");
        var updatedCallerFileContents = await File.ReadAllTextAsync(
            fixture.CallerFilePath,
            TestContext.Current.CancellationToken);
        updatedCallerFileContents.Should().Contain("feature.Updated()");
    }

    [Fact]
    public async Task WhenPersistedStartLineFallsOutsideTheDocument_ThenItReturnsFailureWithoutChangingFiles()
    {
        using var fixture = TemporaryRepository.Create();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var renamer = scope.ServiceProvider.GetRequiredService<IWorkspaceRenamer>();

        var result = await renamer.RenameSymbol(
            new NodeRefactorTarget(
                1,
                "Feature.cs",
                50,
                60,
                "Fixture.csproj",
                DocumentKind.Source),
            "Updated",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("falls outside");
        var sourceFileContents = await File.ReadAllTextAsync(
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        sourceFileContents.Should().Contain("Original()");
        var callerFileContents = await File.ReadAllTextAsync(
            fixture.CallerFilePath,
            TestContext.Current.CancellationToken);
        callerFileContents.Should().Contain("feature.Original()");
    }

    [Fact]
    public async Task WhenTargetDocumentIsMarkdown_ThenItRenamesTheHeadingLocally()
    {
        using var fixture = TemporaryRepository.CreateMarkdownOnly();
        var fileSystem = new FileSystem();
        await using var serviceProvider = CreateServiceProvider(fixture.RootPath, fileSystem);
        await using var scope = serviceProvider.CreateAsyncScope();
        var renamer = scope.ServiceProvider.GetRequiredService<IWorkspaceRenamer>();

        var result = await renamer.RenameSymbol(
            new NodeRefactorTarget(
                1,
                "docs/Guide.md",
                3,
                5,
                DocumentKind: DocumentKind.Markdown),
            "Updated Heading",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ModifiedFilePaths.Should().Equal("docs/Guide.md");
        var updatedFileContents = await File.ReadAllTextAsync(
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        updatedFileContents.ReplaceLineEndings("\n").Should().Be(
            """
            # Guide

            ## Updated Heading

            Original body
            """.ReplaceLineEndings("\n"));
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
            string sourceFilePath,
            string callerFilePath)
        {
            RootPath = rootPath;
            ProjectFilePath = projectFilePath;
            SourceFilePath = sourceFilePath;
            CallerFilePath = callerFilePath;
        }

        public string RootPath { get; }

        public string ProjectFilePath { get; }

        public string SourceFilePath { get; }

        public string CallerFilePath { get; }

        public static TemporaryRepository Create()
        {
            var rootPath = CreateRootPath();
            var projectFilePath = Path.Combine(rootPath, "Fixture.csproj");
            var sourceFilePath = Path.Combine(rootPath, "Feature.cs");
            var callerFilePath = Path.Combine(rootPath, "FeatureCaller.cs");
            WriteProjectFile(projectFilePath);
            WriteSourceFiles(
                sourceFilePath,
                callerFilePath);

            return new TemporaryRepository(
                rootPath,
                projectFilePath,
                sourceFilePath,
                callerFilePath);
        }

        public static TemporaryRepository CreateWithMultipleProjects()
        {
            var rootPath = CreateRootPath();
            var projectFilePath = Path.Combine(rootPath, "App.csproj");
            var sourceFilePath = Path.Combine(rootPath, "Feature.cs");
            var callerFilePath = Path.Combine(rootPath, "FeatureCaller.cs");
            WriteProjectFile(projectFilePath);
            File.WriteAllText(
                Path.Combine(rootPath, "Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            WriteSourceFiles(
                sourceFilePath,
                callerFilePath);

            return new TemporaryRepository(
                rootPath,
                projectFilePath,
                sourceFilePath,
                callerFilePath);
        }

        public static TemporaryRepository CreateMarkdownOnly()
        {
            var rootPath = CreateRootPath();
            var docsDirectoryPath = Path.Combine(rootPath, "docs");
            Directory.CreateDirectory(docsDirectoryPath);
            var sourceFilePath = Path.Combine(docsDirectoryPath, "Guide.md");
            File.WriteAllText(
                sourceFilePath,
                """
                # Guide

                ## Original Heading

                Original body
                """);

            return new TemporaryRepository(
                rootPath,
                Path.Combine(rootPath, "unused.csproj"),
                sourceFilePath,
                sourceFilePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string CreateRootPath()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "sharpsense-refactor-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, ".git"));
            File.WriteAllText(Path.Combine(rootPath, ".git", "HEAD"), "ref: refs/heads/main");
            return rootPath;
        }

        private static void WriteProjectFile(string projectFilePath)
        {
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
        }

        private static void WriteSourceFiles(
            string sourceFilePath,
            string callerFilePath)
        {
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
            File.WriteAllText(
                callerFilePath,
                """
                namespace Fixture;

                public sealed class FeatureCaller
                {
                    public string Call(Feature feature)
                    {
                        return feature.Original();
                    }
                }
                """);
        }
    }
}
