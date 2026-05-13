using System.IO.Abstractions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class WorkspaceLoaderTests
{
    [Fact]
    public async Task WhenModifiedDocumentReadFailsTransiently_ThenRetriesWithoutReloadingWorkspace()
    {
        using var fixture = TemporaryProject.Create();
        var workspaceFactory = new CountingMsBuildWorkspaceFactory();
        var readAttempts = 0;
        var fileSystem = CreateFileSystem(
            async (path, ct) =>
            {
                readAttempts++;

                if (readAttempts == 1)
                {
                    throw new IOException("Transient file read failure.");
                }

                return await File.ReadAllTextAsync(path, ct);
            });
        using var loader = new WorkspaceLoader(workspaceFactory, fileSystem);

        await loader.Load(
            fixture.ProjectFilePath,
            ct: TestContext.Current.CancellationToken);
        fixture.WriteUpdatedSource();

        var result = await loader.UpdateDocuments(
            fixture.ProjectFilePath,
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: fixture.SourceFilePath)
            ],
            TestContext.Current.CancellationToken);

        workspaceFactory.CreateCount.Should().Be(1);
        readAttempts.Should().Be(2);
        var updatedContents = await ReadDocumentContents(
            result.Solution,
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        updatedContents.Should().Contain("Updated()");
    }

    [Fact]
    public async Task WhenModifiedDocumentReadKeepsFailing_ThenReloadsWorkspaceInsteadOfThrowing()
    {
        using var fixture = TemporaryProject.Create();
        var workspaceFactory = new CountingMsBuildWorkspaceFactory();
        var readAttempts = 0;
        var fileSystem = CreateFileSystem(
            (_, _) =>
            {
                readAttempts++;
                throw new IOException("Persistent file read failure.");
            });
        using var loader = new WorkspaceLoader(workspaceFactory, fileSystem);

        await loader.Load(
            fixture.ProjectFilePath,
            ct: TestContext.Current.CancellationToken);
        fixture.WriteUpdatedSource();

        var result = await loader.UpdateDocuments(
            fixture.ProjectFilePath,
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: fixture.SourceFilePath)
            ],
            TestContext.Current.CancellationToken);

        workspaceFactory.CreateCount.Should().Be(2);
        readAttempts.Should().Be(6);
        var updatedContents = await ReadDocumentContents(
            result.Solution,
            fixture.SourceFilePath,
            TestContext.Current.CancellationToken);
        updatedContents.Should().Contain("Updated()");
    }

    [Fact]
    public async Task WhenDocumentIsDeleted_ThenItRemovesTheDocumentWithoutReloadingWorkspace()
    {
        using var fixture = TemporaryProject.Create();
        var workspaceFactory = new CountingMsBuildWorkspaceFactory();
        var fileSystem = CreateFileSystem((path, ct) => File.ReadAllTextAsync(path, ct));
        using var loader = new WorkspaceLoader(workspaceFactory, fileSystem);

        await loader.Load(
            fixture.ProjectFilePath,
            ct: TestContext.Current.CancellationToken);
        fixture.DeleteSource();

        var result = await loader.UpdateDocuments(
            fixture.ProjectFilePath,
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Deleted,
                    OldPath: fixture.SourceFilePath)
            ],
            TestContext.Current.CancellationToken);

        workspaceFactory.CreateCount.Should().Be(1);
        SolutionContainsDocument(result.Solution, fixture.SourceFilePath).Should().BeFalse();
    }

    private static IFileSystem CreateFileSystem(Func<string, CancellationToken, Task<string>> readContents)
    {
        var realFileSystem = new FileSystem();
        var file = new Mock<IFile>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);

        file.Setup(candidate => candidate.Exists(It.IsAny<string>()))
            .Returns((string candidatePath) => File.Exists(candidatePath));
        file.Setup(candidate => candidate.ReadAllTextAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string candidatePath, CancellationToken ct) => readContents(candidatePath, ct));
        fileSystem.SetupGet(candidate => candidate.File)
            .Returns(file.Object);
        fileSystem.SetupGet(candidate => candidate.Path)
            .Returns(realFileSystem.Path);

        return fileSystem.Object;
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

        var documentContents = await document.GetTextAsync(ct);
        return documentContents.ToString();
    }

    private static bool SolutionContainsDocument(
        Solution solution,
        string sourceFilePath)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        return solution.Projects
            .SelectMany(static project => project.Documents)
            .Any(candidate => candidate.FilePath is not null &&
                              pathComparer.Equals(Path.GetFullPath(candidate.FilePath), sourceFilePath));
    }

    private sealed class CountingMsBuildWorkspaceFactory : IMsBuildWorkspaceFactory
    {
        private readonly MsBuildWorkspaceFactory _innerFactory = new();

        public int CreateCount { get; private set; }

        public MSBuildWorkspace Create()
        {
            CreateCount++;
            return _innerFactory.Create();
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(
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

        public static TemporaryProject Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "sharpsense-workspace-loader-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);

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

            return new TemporaryProject(rootPath, projectFilePath, sourceFilePath);
        }

        public void WriteUpdatedSource()
        {
            File.WriteAllText(
                SourceFilePath,
                """
                namespace Fixture;

                public sealed class Feature
                {
                    public string Updated()
                    {
                        return "updated";
                    }
                }
                """);
        }

        public void DeleteSource()
            => File.Delete(SourceFilePath);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
