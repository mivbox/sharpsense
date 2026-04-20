using System.Security.Cryptography;
using System.Text;

namespace SharpSense.Infrastructure.Storage;

public sealed class RepositoryHashCalculator
{
    public static string ComputeHash(string repositoryRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootPath);

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRootPath));
        var hashInput = NormalizeForHash(normalizedPath);
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(hashInput));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string NormalizeForHash(string path)
    {
        var normalizedPath = path.Replace('\\', '/');

        if (OperatingSystem.IsWindows() && normalizedPath.Length >= 2 && normalizedPath[1] == ':')
        {
            normalizedPath = char.ToUpperInvariant(normalizedPath[0]) + normalizedPath[1..];
        }

        return normalizedPath;
    }
}
