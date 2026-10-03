using System.IO.Abstractions;
using GitIgnore = Ignore.Ignore;

namespace SharpSense.Infrastructure.Indexing;

internal sealed class WorkspaceIgnoreRules
{
    private readonly IFileSystem _fileSystem;
    private readonly string _ignorePath;
    private readonly object _gate = new();
    private GitIgnore _rules = new();

    public WorkspaceIgnoreRules(IFileSystem fileSystem, string repositoryRoot)
    {
        _fileSystem = fileSystem;
        _ignorePath = fileSystem.Path.Combine(repositoryRoot, ".gitignore");
        Reload();
    }

    public void Reload()
    {
        var rules = new GitIgnore();
        if (_fileSystem.File.Exists(_ignorePath))
        {
            rules.Add(_fileSystem.File.ReadAllLines(_ignorePath));
        }

        lock (_gate)
        {
            _rules = rules;
        }
    }

    public bool IsIgnored(string relativePath, bool directory = false)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var path = relativePath.Replace('\\', '/');
        lock (_gate)
        {
            return _rules.IsIgnored(directory ? path.TrimEnd('/') + "/" : path);
        }
    }
}
