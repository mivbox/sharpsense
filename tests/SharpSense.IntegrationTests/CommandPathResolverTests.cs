using AwesomeAssertions;
using SharpSense.Cli.Shared;

namespace SharpSense.IntegrationTests;

public sealed class CommandPathResolverTests
{
    [Fact]
    public void WhenResolveRepositoryRootUsesRelativePath_ThenItUsesPwdAsTheBaseDirectory()
    {
        var originalWorkingDirectory = Environment.GetEnvironmentVariable("PWD");
        var workingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src"));

        Environment.SetEnvironmentVariable("PWD", workingDirectory);

        try
        {
            var resolvedRepositoryRoot = CommandPathResolver.ResolveRepositoryRoot("../tests");

            resolvedRepositoryRoot.Should().Be(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(workingDirectory, "../tests"))));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PWD", originalWorkingDirectory);
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
}
