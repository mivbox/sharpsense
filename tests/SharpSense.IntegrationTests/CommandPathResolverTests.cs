using AwesomeAssertions;
using SharpSense.Cli.Shared;

namespace SharpSense.IntegrationTests;

public sealed class CommandPathResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("../tests")]
    public void RepositoryPathsUseTheActualWorkingDirectoryDespiteStalePwd(string? repositoryRoot)
    {
        var previousDirectory = Environment.CurrentDirectory;
        var previousPwd = Environment.GetEnvironmentVariable("PWD");
        var workingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src"));

        try
        {
            Environment.CurrentDirectory = workingDirectory;
            Environment.SetEnvironmentVariable("PWD", Path.GetDirectoryName(workingDirectory));

            var resolvedRepositoryRoot = CommandPathResolver.ResolveRepositoryRoot(repositoryRoot);

            resolvedRepositoryRoot.Should().Be(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot ?? ".", Environment.CurrentDirectory)));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable("PWD", previousPwd);
        }
    }

    [Fact]
    public void WhenResolveTargetDirectoryUsesRelativeTargetPath_ThenItUsesRepositoryRoot()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

        var targetDirectory = CommandPathResolver.ResolveTargetDirectory(
            repositoryRoot,
            "tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture.sln");

        targetDirectory.Should().Be(Path.Combine(repositoryRoot, "tests/SharpSense.IntegrationTests/Assets"));
    }

    [Fact]
    public void WhenResolveTargetDirectoryUsesDirectoryTarget_ThenItReturnsTheDirectory()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

        var targetDirectory = CommandPathResolver.ResolveTargetDirectory(
            repositoryRoot,
            "tests/SharpSense.IntegrationTests/Assets");

        targetDirectory.Should().Be(Path.Combine(repositoryRoot, "tests/SharpSense.IntegrationTests/Assets"));
    }
}
