namespace SharpSense.Application.Tests;

public sealed class ApplicationAssemblyMarkerTests
{
    [Fact]
    public void WhenResolvingApplicationAssemblyNameFromMarker_ThenReturnsSharpSenseApplication()
    {
        var assemblyName = typeof(ApplicationAssemblyMarker).Assembly.GetName().Name;

        Assert.NotNull(assemblyName);
        Assert.Equal("SharpSense.Application", assemblyName);
    }
}
