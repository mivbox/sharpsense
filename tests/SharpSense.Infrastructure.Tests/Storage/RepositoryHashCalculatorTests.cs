using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class RepositoryHashCalculatorTests
{
    [Fact]
    public void WhenComputeHashUsesEquivalentPaths_ThenReturnsSameHash()
    {
        var repositoryRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var rootedVariant = repositoryRoot + Path.DirectorySeparatorChar;
        var dottedVariant = Path.Combine(repositoryRoot, ".");

        var repositoryHash = RepositoryHashCalculator.ComputeHash(repositoryRoot);
        var rootedVariantHash = RepositoryHashCalculator.ComputeHash(rootedVariant);
        var dottedVariantHash = RepositoryHashCalculator.ComputeHash(dottedVariant);

        Assert.Equal(repositoryHash, rootedVariantHash);
        Assert.Equal(repositoryHash, dottedVariantHash);
    }
}
