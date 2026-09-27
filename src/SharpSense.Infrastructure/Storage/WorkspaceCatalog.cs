using System.IO.Abstractions;
using Serilog;
using SharpSense.Application.Indexing;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// Reads and maintains named workspaces under the SharpSense home directory.
/// Listing and resolving never create directories, databases, or configuration files.
/// </summary>
public sealed class WorkspaceCatalog
{
    private static readonly ILogger Logger = Log.ForContext<WorkspaceCatalog>();
    private readonly IFileSystem _fileSystem;
    private readonly object _deserializationGate = new();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    public WorkspaceCatalog(IFileSystem fileSystem, string? homeDirectory = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        HomeDirectory = SharpSenseHome.Resolve(fileSystem, homeDirectory);
    }

    public string HomeDirectory { get; }

    public Guid? GetDefaultWorkspaceId()
    {
        var path = _fileSystem.Path.Combine(HomeDirectory, "default-workspace");
        if (!_fileSystem.File.Exists(path))
        {
            return null;
        }

        using var stream = _fileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        if (!Guid.TryParse(reader.ReadToEnd().Trim(), out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Default workspace selection '{path}' is invalid. Run 'sharpsense workspace use <name-or-id>' to replace it, or pass --workspace explicitly.");
        }

        return id;
    }

    public WorkspaceSelection Use(string nameOrId)
    {
        using var catalogLock = AcquireWriteLock();
        var selection = ResolveExplicit(List(), nameOrId);
        WriteAtomically(_fileSystem.Path.Combine(HomeDirectory, "default-workspace"), selection.Definition.Id.ToString("D"));
        return selection;
    }

    public IDisposable AcquireIndexLease(WorkspaceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var expected = CreateSelection(selection.Definition);
        if (!PathsEqual(expected.DirectoryPath, selection.DirectoryPath) ||
            !_fileSystem.File.Exists(expected.ConfigurationPath))
        {
            throw new WorkspaceDefinitionChangedException("Workspace registration changed or belongs to another SharpSense home. Resolve the workspace again before indexing.");
        }

        var lease = WorkspaceIndexLease.Acquire(_fileSystem, expected.DirectoryPath, selection.Definition.Name);
        try
        {
            var current = Read(expected.ConfigurationPath, selection.Definition.Id).Definition;
            var selected = selection.Definition;
            if (current.Version != selected.Version || current.Id != selected.Id ||
                !string.Equals(current.Name, selected.Name, StringComparison.Ordinal) ||
                !PathsEqual(current.RepositoryRoot, selected.RepositoryRoot) ||
                !current.Sources.SequenceEqual(selected.Sources))
            {
                throw new WorkspaceDefinitionChangedException(
                    $"Workspace '{selected.Name}' changed after this command selected it. Restart indexing or watching to use the current sources.");
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public IReadOnlyList<WorkspaceSelection> List()
    {
        var workspacesDirectory = _fileSystem.Path.Combine(HomeDirectory, "workspaces");
        if (!_fileSystem.Directory.Exists(workspacesDirectory))
        {
            return [];
        }

        var selections = new List<WorkspaceSelection>();
        foreach (var directory in _fileSystem.Directory.EnumerateDirectories(workspacesDirectory))
        {
            var directoryName = _fileSystem.Path.GetFileName(directory);
            if (!Guid.TryParseExact(directoryName, "D", out var id))
            {
                continue;
            }

            var configurationPath = _fileSystem.Path.Combine(directory, "workspace.yaml");
            if (_fileSystem.File.Exists(configurationPath))
            {
                try
                {
                    selections.Add(Read(configurationPath, id));
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    // Malformed YAML can include private source content in its exception.
                    // Report only the affected registration and safe failure category.
                    Logger.Warning("Skipping unavailable workspace configuration {ConfigurationPath} ({FailureType}). Resolve workspace ID {WorkspaceId} for details.",
                        configurationPath, (exception.InnerException ?? exception).GetType().Name, id);
                }
            }
        }

        return selections.OrderBy(static selection => selection.Definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public WorkspaceSelection Resolve(string? nameOrId, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (nameOrId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
            return Guid.TryParse(nameOrId.Trim(), out var id)
                ? ResolveById(id)
                : ResolveExplicit(List(), nameOrId);
        }

        if (GetDefaultWorkspaceId() is { } defaultId)
        {
            try
            {
                return ResolveById(defaultId);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Default workspace '{defaultId}' is unavailable. Run 'sharpsense workspace use <name-or-id>' to choose another, or pass --workspace explicitly.");
            }
        }

        var repositoryRoot = RepositoryWorkspace.ResolveRootPathFromWorkingDirectory(workingDirectory, _fileSystem);
        var hasGitRoot = _fileSystem.Directory.Exists(_fileSystem.Path.Combine(repositoryRoot, ".git")) ||
                         _fileSystem.File.Exists(_fileSystem.Path.Combine(repositoryRoot, ".git"));
        var matches = List().Where(selection =>
                PathsEqual(selection.Workspace.RootPath, repositoryRoot) ||
                (!hasGitRoot && selection.Workspace.IsSameOrSubPath(repositoryRoot)))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No workspace is registered for '{repositoryRoot}'. Run 'sharpsense workspace create', or select an existing workspace with '--workspace <name>'."),
            _ => throw new InvalidOperationException(
                $"Multiple workspaces match '{repositoryRoot}': {string.Join(", ", matches.Select(static selection => selection.Definition.Name))}. Select one with '--workspace <name-or-id>'.")
        };
    }

    public WorkspaceSelection ResolveById(Guid id)
    {
        var configurationPath = _fileSystem.Path.Combine(HomeDirectory, "workspaces", id.ToString("D"), "workspace.yaml");
        if (!_fileSystem.File.Exists(configurationPath))
        {
            throw new KeyNotFoundException($"Workspace '{id:D}' was not found. Run 'sharpsense workspace list' to see registered workspaces.");
        }

        return Read(configurationPath, id);
    }

    public WorkspaceSelection Create(
        string name,
        string repositoryRoot,
        IEnumerable<WorkspaceSource> sources)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(sources);

        if (!_fileSystem.Path.IsPathRooted(repositoryRoot))
        {
            throw new ArgumentException("Repository root must be an absolute directory path.", nameof(repositoryRoot));
        }

        if (!_fileSystem.Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository directory '{repositoryRoot}' was not found.");
        }

        var definition = new WorkspaceDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            RepositoryRoot = RepositoryWorkspace.ResolveRootPathFromWorkingDirectory(repositoryRoot, _fileSystem),
            Sources = sources.ToArray()
        };

        NormalizeDefinition(definition, requireExistingSources: true, sourceBasePath: repositoryRoot);

        using var catalogLock = AcquireWriteLock();
        EnsureNameAvailable(definition.Name);
        Write(definition);
        return CreateSelection(definition);
    }

    public WorkspaceSelection AddSources(string nameOrId, IEnumerable<WorkspaceSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        using var catalogLock = AcquireWriteLock();
        var selection = ResolveExplicit(List(), nameOrId);
        using var indexLease = AcquireIndexLease(selection);
        selection.Definition.Sources = selection.Definition.Sources.Concat(sources).ToArray();
        NormalizeDefinition(selection.Definition, requireExistingSources: true);
        Write(selection.Definition);
        return CreateSelection(selection.Definition);
    }

    public WorkspaceSelection Rename(string nameOrId, string name)
    {
        ValidateName(name);
        using var catalogLock = AcquireWriteLock();
        var existing = List();
        var selection = ResolveExplicit(existing, nameOrId);
        using var indexLease = AcquireIndexLease(selection);
        EnsureNameAvailable(name.Trim(), existing.Where(item => item.Definition.Id != selection.Definition.Id).ToArray());
        selection.Definition.Name = name.Trim();
        Write(selection.Definition);
        return CreateSelection(selection.Definition);
    }

    public WorkspaceSelection Update(string nameOrId, string name, IEnumerable<WorkspaceSource> sources)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(sources);
        using var catalogLock = AcquireWriteLock();
        var existing = List();
        var selection = ResolveExplicit(existing, nameOrId);
        using var indexLease = AcquireIndexLease(selection);
        EnsureNameAvailable(name.Trim(), existing.Where(item => item.Definition.Id != selection.Definition.Id).ToArray());
        selection.Definition.Name = name.Trim();
        selection.Definition.Sources = sources.ToArray();
        NormalizeDefinition(selection.Definition, requireExistingSources: true);
        Write(selection.Definition);
        return CreateSelection(selection.Definition);
    }

    public WorkspaceSourceRemoval RemoveSources(string nameOrId, IEnumerable<WorkspaceSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        using var catalogLock = AcquireWriteLock();
        var selection = ResolveExplicit(List(), nameOrId);
        using var indexLease = AcquireIndexLease(selection);
        var requested = NormalizeSources(selection.Definition.RepositoryRoot, sources, requireExistingSources: false);
        var existing = selection.Definition.Sources;
        var removed = existing.Where(source => requested.Any(candidate => SameSource(source, candidate))).ToArray();
        var unmatched = requested.Where(source => !existing.Any(candidate => SameSource(source, candidate))).ToArray();

        if (removed.Length > 0)
        {
            selection.Definition.Sources = existing.Where(source => !removed.Contains(source)).ToArray();
            Write(selection.Definition);
        }

        return new WorkspaceSourceRemoval(CreateSelection(selection.Definition), removed, unmatched);
    }

    public WorkspaceSelection Merge(string name, IEnumerable<string> workspaceNames)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(workspaceNames);
        using var catalogLock = AcquireWriteLock();
        var existing = List();
        var selections = workspaceNames.Select(selector => ResolveExplicit(existing, selector))
            .DistinctBy(static selection => selection.Definition.Id)
            .ToArray();
        if (selections.Length < 2)
        {
            throw new ArgumentException("Select at least two workspaces to merge.", nameof(workspaceNames));
        }

        var repositoryRoot = selections[0].Definition.RepositoryRoot;
        if (selections.Any(selection => !PathsEqual(selection.Workspace.RootPath, repositoryRoot)))
        {
            throw new InvalidOperationException("Workspaces can only be merged when they belong to the same repository checkout.");
        }

        EnsureNameAvailable(name.Trim(), existing);
        var definition = new WorkspaceDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            RepositoryRoot = repositoryRoot,
            Sources = selections.SelectMany(static selection => selection.Definition.Sources).ToArray()
        };

        NormalizeDefinition(definition, requireExistingSources: false);
        Write(definition);
        return CreateSelection(definition);
    }

    private WorkspaceSelection Read(string configurationPath, Guid expectedId)
    {
        try
        {
            // Allow atomic replacement while a concurrent request reads the preceding snapshot, including on Windows.
            using var stream = _fileSystem.FileStream.New(configurationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            WorkspaceDefinition definition;
            lock (_deserializationGate)
            {
                definition = _deserializer.Deserialize<WorkspaceDefinition>(reader)
                    ?? throw new InvalidOperationException("Workspace configuration is empty.");
            }
            if (definition.Id != expectedId)
            {
                throw new InvalidOperationException("Workspace ID must match its storage directory.");
            }

            NormalizeDefinition(definition, requireExistingSources: false);
            return CreateSelection(definition);
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Workspace configuration '{configurationPath}' is invalid: {exception.Message}", exception);
        }
    }

    private WorkspaceSelection CreateSelection(WorkspaceDefinition definition)
    {
        var directory = _fileSystem.Path.Combine(HomeDirectory, "workspaces", definition.Id.ToString("D"));
        var databasePath = _fileSystem.Path.Combine(directory, "index.db");
        return new WorkspaceSelection(
            definition,
            directory,
            _fileSystem.Path.Combine(directory, "workspace.yaml"),
            new RepositoryWorkspace(definition.RepositoryRoot, databasePath, _fileSystem, definition));
    }

    private void NormalizeDefinition(WorkspaceDefinition definition, bool requireExistingSources, string? sourceBasePath = null)
    {
        if (definition.Version != 1)
        {
            throw new InvalidOperationException($"Workspace version '{definition.Version}' is unsupported; expected version 1.");
        }

        if (definition.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Workspace ID must be a non-empty GUID.");
        }

        ValidateName(definition.Name);
        definition.Name = definition.Name.Trim();
        if (!_fileSystem.Path.IsPathRooted(definition.RepositoryRoot))
        {
            throw new InvalidOperationException("Workspace repositoryRoot must be an absolute path.");
        }

        definition.RepositoryRoot = RepositoryWorkspace.NormalizeRootPath(definition.RepositoryRoot, _fileSystem);
        definition.Sources = NormalizeSources(definition.RepositoryRoot, definition.Sources ?? [], requireExistingSources, sourceBasePath);
    }

    private WorkspaceSource[] NormalizeSources(
        string repositoryRoot,
        IEnumerable<WorkspaceSource> sources,
        bool requireExistingSources,
        string? sourceBasePath = null)
    {
        var workspace = new RepositoryWorkspace(repositoryRoot, _fileSystem.Path.Combine(HomeDirectory, "unused.db"), _fileSystem);
        var normalized = new List<WorkspaceSource>();
        foreach (var source in sources)
        {
            if (source is null || !Enum.IsDefined(source.Kind))
            {
                throw new ArgumentException("Workspace source kind must be cSharp, typeScript, or markdown.", nameof(sources));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(source.Path);
            var path = source.Path.Trim().Replace('\\', '/');
            if (path.Contains('\0') || path.Split('/').Contains("..", StringComparer.Ordinal))
            {
                throw new ArgumentException($"Workspace source '{path}' must stay inside the repository root.", nameof(sources));
            }

            var fullPath = _fileSystem.Path.IsPathRooted(path)
                ? _fileSystem.Path.GetFullPath(path)
                : _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(sourceBasePath ?? repositoryRoot, path));
            if (!workspace.TryToRepositoryRelativePath(fullPath, out var relativePath))
            {
                throw new ArgumentException($"Workspace source '{path}' must stay inside the repository root.", nameof(sources));
            }

            relativePath = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
            ValidateSourceTarget(source.Kind, fullPath, requireExistingSources);
            var normalizedSource = new WorkspaceSource(source.Kind, relativePath);
            if (!normalized.Any(existing => SameSource(existing, normalizedSource)))
            {
                normalized.Add(normalizedSource);
            }
        }

        return normalized.ToArray();
    }

    private void ValidateSourceTarget(WorkspaceSourceKind kind, string fullPath, bool requireExistingSources)
    {
        var extension = _fileSystem.Path.GetExtension(fullPath);
        if (kind == WorkspaceSourceKind.CSharp &&
            !new[] { ".csproj", ".sln", ".slnx" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"C# source '{fullPath}' must be a .csproj, .sln, or .slnx file.");
        }

        if (requireExistingSources &&
            ((kind == WorkspaceSourceKind.CSharp && !_fileSystem.File.Exists(fullPath)) ||
             (kind == WorkspaceSourceKind.TypeScript && !_fileSystem.File.Exists(fullPath) && !_fileSystem.Directory.Exists(fullPath))))
        {
            throw new FileNotFoundException($"Workspace source '{fullPath}' was not found.", fullPath);
        }

        if (kind == WorkspaceSourceKind.TypeScript && _fileSystem.File.Exists(fullPath) &&
            (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
             !_fileSystem.Path.GetFileName(fullPath).StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"TypeScript source '{fullPath}' must be a directory or a tsconfig JSON file.");
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 100 || trimmed.Any(char.IsControl) || trimmed.IndexOfAny(['/', '\\']) >= 0 ||
            trimmed is "." or ".." || Guid.TryParse(trimmed, out _))
        {
            throw new ArgumentException("Workspace name must be at most 100 characters, contain no path separators or control characters, and not be a GUID.", nameof(name));
        }
    }

    private WorkspaceSelection ResolveExplicit(IReadOnlyList<WorkspaceSelection> selections, string nameOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
        var selector = nameOrId.Trim();
        var isId = Guid.TryParse(selector, out var id);
        if (isId)
        {
            return ResolveById(id);
        }

        var matches = selections.Where(selection => string.Equals(selection.Definition.Name, selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Workspace '{selector}' was not found. Run 'sharpsense workspace list' to see registered workspaces."),
            _ => throw new InvalidOperationException($"Workspace name '{selector}' is ambiguous. Select a workspace by ID from 'sharpsense workspace list'.")
        };
    }

    private void EnsureNameAvailable(string name, IReadOnlyList<WorkspaceSelection>? selections = null)
    {
        if ((selections ?? List()).Any(selection => string.Equals(selection.Definition.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Workspace '{name}' already exists. Choose another name or add sources to the existing workspace.");
        }
    }

    private IDisposable AcquireWriteLock()
    {
        _fileSystem.Directory.CreateDirectory(HomeDirectory);
        try
        {
            return _fileSystem.FileStream.New(
                _fileSystem.Path.Combine(HomeDirectory, ".workspaces.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Another process is updating the workspace catalog. Retry this command when it completes.", exception);
        }
    }

    private void Write(WorkspaceDefinition definition)
    {
        var selection = CreateSelection(definition);
        _fileSystem.Directory.CreateDirectory(selection.DirectoryPath);
        WriteAtomically(selection.ConfigurationPath, _serializer.Serialize(definition));
    }

    private void WriteAtomically(string destination, string content)
    {
        var temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            _fileSystem.File.WriteAllText(temporaryPath, content);
            _fileSystem.File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private static bool PathsEqual(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SameSource(WorkspaceSource left, WorkspaceSource right) =>
        left.Kind == right.Kind && PathsEqual(left.Path, right.Path);
}
