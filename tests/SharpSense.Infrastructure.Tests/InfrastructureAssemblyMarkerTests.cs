namespace SharpSense.Infrastructure.Tests;

public sealed class InfrastructureAssemblyMarkerTests
{
    [Fact]
    public void WhenResolvingInfrastructureAssemblyNameFromMarker_ThenReturnsSharpSenseInfrastructure()
    {
        var assemblyName = typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name;

        Assert.NotNull(assemblyName);
        Assert.Equal("SharpSense.Infrastructure", assemblyName);
    }
}
