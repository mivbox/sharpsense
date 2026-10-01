using AwesomeAssertions;
using Microsoft.Build.Locator;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class MsBuildLocatorRegistrationTests
{
    [Theory]
    [InlineData(9, "Zulu", DiscoveryType.DotNetSdk, "/sdk/z", 8, "Alpha", DiscoveryType.DeveloperConsole, "/sdk/a")]
    [InlineData(8, "Alpha", DiscoveryType.DotNetSdk, "/sdk/z", 8, "Zulu", DiscoveryType.DeveloperConsole, "/sdk/a")]
    [InlineData(8, "Alpha", DiscoveryType.DeveloperConsole, "/sdk/z", 8, "Alpha", DiscoveryType.DotNetSdk, "/sdk/a")]
    [InlineData(8, "Alpha", DiscoveryType.DotNetSdk, "/sdk/a", 8, "Alpha", DiscoveryType.DotNetSdk, "/sdk/b")]
    public void WhenSelectingPreferredInstance_ThenAppliesEachTieBreakerInOrder(
        int preferredVersion,
        string preferredName,
        DiscoveryType preferredDiscovery,
        string preferredPath,
        int otherVersion,
        string otherName,
        DiscoveryType otherDiscovery,
        string otherPath)
    {
        var preferred = new MsBuildInstanceCandidate(preferredName, preferredPath, new Version(preferredVersion, 0), preferredDiscovery);
        var other = new MsBuildInstanceCandidate(otherName, otherPath, new Version(otherVersion, 0), otherDiscovery);

        var selected = MsBuildLocatorRegistration.SelectPreferredInstance([other, preferred]);

        selected.Should().Be(preferred);
    }

    [Fact]
    public void WhenNoMsBuildInstancesAreAvailable_ThenThrowsInvalidOperationException()
    {
        var act = () => MsBuildLocatorRegistration.SelectPreferredInstance([]);

        act.Should().ThrowExactly<InvalidOperationException>();
    }
}
