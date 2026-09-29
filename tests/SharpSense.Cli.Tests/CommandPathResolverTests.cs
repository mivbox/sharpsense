using AwesomeAssertions;
using SharpSense.Cli.Shared;

namespace SharpSense.Cli.Tests;

[Collection("Process state")]
public sealed class CommandPathResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("../tests")]
    public void WhenRepositoryPaths_ThenUseTheActualWorkingDirectoryDespiteStalePwd(string? repositoryRoot)
    {
        var previousDirectory = Environment.CurrentDirectory;
        var previousPwd = Environment.GetEnvironmentVariable("PWD");
        var workingDirectory = Directory.CreateTempSubdirectory("sharpsense-cli-").FullName;

        try
        {
            Environment.CurrentDirectory = workingDirectory;
            Environment.SetEnvironmentVariable("PWD", Path.GetDirectoryName(workingDirectory));

            var resolvedRepositoryRoot = CommandPathResolver.ResolveWorkspaceRoot(repositoryRoot);

            resolvedRepositoryRoot.Should().Be(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot ?? ".", Environment.CurrentDirectory)));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable("PWD", previousPwd);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
