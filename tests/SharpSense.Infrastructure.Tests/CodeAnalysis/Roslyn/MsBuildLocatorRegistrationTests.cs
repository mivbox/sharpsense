using AwesomeAssertions;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class MsBuildLocatorRegistrationTests
{
    [Fact]
    public void WhenSelectingPreferredInstance_ThenPrefersHighestVersionNameDiscoveryTypeAndPath()
    {
        var instances = new[]
        {
            new MsBuildInstanceCandidate("Zulu", "/sdk/zulu", new Version(8, 0, 200), Microsoft.Build.Locator.DiscoveryType.DotNetSdk),
            new MsBuildInstanceCandidate("Alpha", "/sdk/alpha-b", new Version(8, 0, 200), Microsoft.Build.Locator.DiscoveryType.VisualStudioSetup),
            new MsBuildInstanceCandidate("Alpha", "/sdk/alpha-a", new Version(8, 0, 200), Microsoft.Build.Locator.DiscoveryType.DeveloperConsole),
            new MsBuildInstanceCandidate("Older", "/sdk/older", new Version(8, 0, 100), Microsoft.Build.Locator.DiscoveryType.DotNetSdk)
        };

        var selected = MsBuildLocatorRegistration.SelectPreferredInstance(instances);

        selected.Name.Should().Be("Alpha");
        selected.DiscoveryType.Should().Be(Microsoft.Build.Locator.DiscoveryType.DeveloperConsole);
        selected.MSBuildPath.Should().Be("/sdk/alpha-a");
    }

    [Fact]
    public void WhenSelectingPreferredInstanceWithoutAvailableInstances_ThenThrowsInvalidOperationException()
    {
        ((Action)(() => MsBuildLocatorRegistration.SelectPreferredInstance(Array.Empty<MsBuildInstanceCandidate>()))).Should().ThrowExactly<InvalidOperationException>();
    }
}
