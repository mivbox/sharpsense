namespace SharpSense.Infrastructure.Indexing.CSharp;

internal interface ICSharpWorkspaceTargetResolver
{
    string? ResolveTargetPath(string targetPath);
}
