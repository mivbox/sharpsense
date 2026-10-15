namespace SharpSense.Testkit;

public static class RepositoryPaths
{
    public static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpSense.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the SharpSense checkout for build tests.");
        }
    }
}
