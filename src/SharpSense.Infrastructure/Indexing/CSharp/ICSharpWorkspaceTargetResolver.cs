namespace SharpSense.Infrastructure.Indexing.CSharp;

public interface ICSharpWorkspaceTargetResolver
{
    string? ResolveTargetPath(string targetPath);
}
