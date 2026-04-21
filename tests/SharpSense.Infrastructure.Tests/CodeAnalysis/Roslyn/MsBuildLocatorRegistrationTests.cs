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

        Assert.Equal("Alpha", selected.Name);
        Assert.Equal(Microsoft.Build.Locator.DiscoveryType.DeveloperConsole, selected.DiscoveryType);
        Assert.Equal("/sdk/alpha-a", selected.MSBuildPath);
    }

    [Fact]
    public void WhenSelectingPreferredInstanceWithoutAvailableInstances_ThenThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => MsBuildLocatorRegistration.SelectPreferredInstance(Array.Empty<MsBuildInstanceCandidate>()));
    }
}
