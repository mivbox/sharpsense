using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

public static class SharpSenseHome
{
    public static string Resolve(IFileSystem fileSystem, string? homeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var selectedHome = homeDirectory ?? Environment.GetEnvironmentVariable("SHARPSENSE_HOME");
        if (string.IsNullOrWhiteSpace(selectedHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new InvalidOperationException("Unable to locate SharpSense storage. Set SHARPSENSE_HOME to an absolute directory path.");
            }

            selectedHome = fileSystem.Path.Combine(userProfile, ".sharpsense");
        }

        if (!fileSystem.Path.IsPathRooted(selectedHome))
        {
            throw new ArgumentException("SHARPSENSE_HOME must be an absolute directory path.", nameof(homeDirectory));
        }

        return fileSystem.Path.GetFullPath(selectedHome);
    }
}
