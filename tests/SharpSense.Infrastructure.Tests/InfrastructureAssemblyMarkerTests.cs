namespace SharpSense.Infrastructure.Tests;

public sealed class InfrastructureAssemblyMarkerTests
{
    [Fact]
    public void InfrastructureAssemblyMarker_can_be_loaded()
    {
        var assemblyName = typeof(SharpSense.Infrastructure.InfrastructureAssemblyMarker).Assembly.GetName().Name;

        Assert.NotNull(assemblyName);
        Assert.Equal("SharpSense.Infrastructure", assemblyName);
    }
}
