namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

internal static class TypeScriptTestPaths
{
    // MockFileSystem follows the host's path rules, including Windows drive roots.
    public static string Absolute(string path)
        => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
}
