namespace SharpSense.Application.Tests;

public sealed class ApplicationAssemblyMarkerTests
{
    [Fact]
    public void ApplicationAssemblyMarker_can_be_loaded()
    {
        var assemblyName = typeof(SharpSense.Application.ApplicationAssemblyMarker).Assembly.GetName().Name;

        Assert.NotNull(assemblyName);
        Assert.Equal("SharpSense.Application", assemblyName);
    }
}
