using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TsConfigResolver(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, ResolvedTsConfig> _configCache = new(FileSystemPaths.Comparer);
    private readonly Dictionary<string, ResolvedTsConfig> _sourceConfigs = new(FileSystemPaths.Comparer);
    private readonly HashSet<string> _resolutionInputPaths = new(FileSystemPaths.Comparer);
    private Dictionary<string, string>? _workspacePackageRootsByName;

    public IReadOnlyCollection<string> ResolutionInputPaths => _resolutionInputPaths;

    public void ClearCache()
    {
        _configCache.Clear();
        _sourceConfigs.Clear();
        _resolutionInputPaths.Clear();
        _workspacePackageRootsByName = null;
    }

    public IReadOnlyList<string> GetReferencedConfigPaths(string targetPath)
    {
        var configPath = GetTargetConfigPath(targetPath);
        if (configPath is null)
        {
            return [];
        }
        var visited = new HashSet<string>(FileSystemPaths.Comparer)
        {
            configPath
        };
        var pending = new Queue<string>();
        pending.Enqueue(configPath);
        var result = new List<string>();
        while (pending.TryDequeue(out var path))
        {
            foreach (var reference in LoadConfig(path).References)
            {
                var referencedConfig = fileSystem.Directory.Exists(reference)
                    ? fileSystem.Path.Combine(reference, "tsconfig.json")
                    : reference;
                if (repositoryWorkspace.IsSameOrSubPath(referencedConfig) && visited.Add(referencedConfig))
                {
                    result.Add(referencedConfig);
                    pending.Enqueue(referencedConfig);
                }
            }
        }

        return result;
    }

    public IReadOnlyList<DiscoveredFile> FilterRootFiles(string targetPath, IReadOnlyList<DiscoveredFile> files)
    {
        var configPath = GetTargetConfigPath(targetPath);
        if (configPath is null)
        {
            return files;
        }
        var config = LoadConfig(configPath);
        var explicitFiles = (config.Files ?? []).ToHashSet(FileSystemPaths.Comparer);
        var includes = config.Includes ?? (config.Files is null ?
        [
            fileSystem.Path.Combine(
                config.ConfigDirectoryPath,
                "**/*")
        ] : []);
        var selectedFiles = files
            .Where(file => explicitFiles.Contains(file.AbsolutePath) ||
                includes.Any(pattern => MatchesPattern(file.AbsolutePath, pattern)) &&
                !config.Excludes.Any(pattern => MatchesPattern(file.AbsolutePath, pattern)))
            .ToArray();
        foreach (var file in selectedFiles)
        {
            // Discovery visits explicit target before references. First owning configuration
            // wins when inputs overlap; referenced project roots bind before following imports.
            _sourceConfigs.TryAdd(GetAbsolutePath(file.AbsolutePath), config);
        }

        return selectedFiles;
    }

    public bool HasTargetConfiguration(string targetPath)
        => GetTargetConfigPath(targetPath) is not null;

    private string? GetTargetConfigPath(string targetPath)
    {
        var absolute = GetAbsolutePath(fileSystem.Path.IsPathRooted(targetPath)
            ? targetPath
            : fileSystem.Path.Combine(repositoryWorkspace.RootPath, targetPath));
        var config = absolute;
        if (!absolute.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (!fileSystem.Directory.Exists(absolute))
            {
                return null;
            }
            config = fileSystem.Path.Combine(absolute, "tsconfig.json");
            if (!fileSystem.File.Exists(config))
            {
                return null;
            }
        }

        // Repository discovery resolves symlinked roots. Config patterns must use that same
        // physical path (for example /var and /private/var on macOS) before matching candidates.
        return fileSystem.Path.Combine(
            repositoryWorkspace.RootPath,
            repositoryWorkspace.ToRepositoryRelativePath(config));
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        pattern = pattern.Replace('\\', '/');
        var lastSegment = pattern[(pattern.LastIndexOf('/') + 1)..];
        if (!lastSegment.Contains('*') && !lastSegment.Contains('?') && !lastSegment.Contains('.'))
        {
            pattern = pattern.TrimEnd('/') + "/**/*";
        }
        var expression = "^" + Regex.Escape(pattern)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]") + "$";

        return Regex.IsMatch(
            path.Replace('\\', '/'),
            expression,
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);
    }

    public string? ResolveImport(
        string importPath,
        string sourceFilePath,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            return ResolveImportCore(importPath, sourceFilePath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ClearCache();
            throw;
        }
    }

    private string? ResolveImportCore(string importPath, string sourceFilePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        var absoluteSourceFilePath = GetAbsolutePath(sourceFilePath);
        var sourceDirectoryPath = fileSystem.Path.GetDirectoryName(absoluteSourceFilePath)
            ?? throw new InvalidOperationException($"Unable to determine the source directory for '{absoluteSourceFilePath}'.");
        var resolvedConfig = _sourceConfigs.GetValueOrDefault(absoluteSourceFilePath) ?? FindNearestConfig(absoluteSourceFilePath);

        if (IsRelativeImport(importPath))
        {
            return BindImportedSource(
                ResolveModulePath(fileSystem.Path.Combine(
                    sourceDirectoryPath,
                    importPath)),
                resolvedConfig);
        }

        if (resolvedConfig is not null)
        {
            foreach (var pathMapping in GetOrderedPathMappings(resolvedConfig.PathMappings))
            {
                if (!TryMatchPattern(pathMapping.Pattern, importPath, out var wildcardValue))
                {
                    continue;
                }

                foreach (var targetPattern in pathMapping.TargetPatterns)
                {
                    var candidate = fileSystem.Path.Combine(
                        resolvedConfig.BaseUrlPath ?? pathMapping.ConfigDirectoryPath,
                        ApplyWildcard(targetPattern, wildcardValue));
                    var resolvedPath = ResolveModulePath(candidate);
                    if (resolvedPath is not null)
                    {
                        return BindImportedSource(resolvedPath, resolvedConfig);
                    }
                }
                // Only the best matching paths key is eligible; its target list supplies fallbacks.
                break;
            }

            if (resolvedConfig.BaseUrlPath is not null)
            {
                var baseUrlResolvedPath = ResolveModulePath(fileSystem.Path.Combine(
                    resolvedConfig.BaseUrlPath,
                    importPath));
                if (baseUrlResolvedPath is not null)
                {
                    return BindImportedSource(baseUrlResolvedPath, resolvedConfig);
                }
            }
        }

        if (TryResolveWorkspacePackageImport(importPath, ct) is { } workspacePackagePath)
        {
            return BindImportedSource(workspacePackagePath, resolvedConfig);
        }

        return null;
    }

    private string? BindImportedSource(string? path, ResolvedTsConfig? config)
    {
        if (path is not null && config is not null)
        {
            _sourceConfigs.TryAdd(path, config);
        }

        return path;
    }

    private ResolvedTsConfig? FindNearestConfig(string sourceFilePath)
    {
        var currentDirectoryPath = fileSystem.Path.GetDirectoryName(sourceFilePath);

        while (!string.IsNullOrWhiteSpace(currentDirectoryPath) &&
               repositoryWorkspace.IsSameOrSubPath(currentDirectoryPath))
        {
            var tsConfigPath = fileSystem.Path.Combine(currentDirectoryPath, "tsconfig.json");
            if (fileSystem.File.Exists(tsConfigPath))
            {
                return LoadConfig(tsConfigPath);
            }

            if (IsRepositoryRoot(currentDirectoryPath))
            {
                break;
            }

            currentDirectoryPath = fileSystem.DirectoryInfo.New(currentDirectoryPath).Parent?.FullName;
        }

        return null;
    }

    private ResolvedTsConfig LoadConfig(string configPath)
    {
        var absoluteConfigPath = GetAbsolutePath(configPath);
        if (_configCache.TryGetValue(absoluteConfigPath, out var cachedConfig))
        {
            return cachedConfig;
        }

        return _configCache[absoluteConfigPath] = LoadConfigCore(
            absoluteConfigPath,
            new HashSet<string>(FileSystemPaths.Comparer));
    }

    private ResolvedTsConfig LoadConfigCore(
        string configPath,
        HashSet<string> visitedConfigPaths)
    {
        var absoluteConfigPath = GetAbsolutePath(configPath);
        if (_configCache.TryGetValue(absoluteConfigPath, out var cachedConfig))
        {
            return cachedConfig;
        }

        if (!visitedConfigPaths.Add(absoluteConfigPath))
        {
            throw new InvalidOperationException($"Detected a circular tsconfig extends chain at '{absoluteConfigPath}'.");
        }

        var configDirectoryPath = fileSystem.Path.GetDirectoryName(absoluteConfigPath)
            ?? throw new InvalidOperationException($"Unable to determine the tsconfig directory for '{absoluteConfigPath}'.");
        var configDocument = ReadConfig(absoluteConfigPath);
        ResolvedTsConfig? baseConfig = null;
        var extendsPaths = configDocument.Extends.ValueKind switch
        {
            JsonValueKind.String => new[]
            {
                configDocument.Extends.GetString()!
            },
            JsonValueKind.Array => configDocument.Extends.EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            _ => []
        };
        foreach (var extendsPath in extendsPaths)
        {
            var inherited = LoadConfigCore(ResolveExtendsPath(configDirectoryPath, extendsPath), visitedConfigPaths);
            baseConfig = baseConfig is null
                ? inherited
                : inherited with
                {
                    BaseUrlPath = inherited.BaseUrlPath ?? baseConfig.BaseUrlPath,
                    PathMappings = inherited.HasPathMappings ? inherited.PathMappings : baseConfig.PathMappings,
                    HasPathMappings = inherited.HasPathMappings || baseConfig.HasPathMappings,
                    Files = inherited.Files ?? baseConfig.Files,
                    Includes = inherited.Includes ?? baseConfig.Includes,
                    Excludes = inherited.HasExcludes ? inherited.Excludes : baseConfig.Excludes,
                    HasExcludes = inherited.HasExcludes || baseConfig.HasExcludes
                };
        }
        var baseUrlPath = !string.IsNullOrWhiteSpace(configDocument.CompilerOptions?.BaseUrl)
            ? GetAbsolutePath(fileSystem.Path.Combine(configDirectoryPath, configDocument.CompilerOptions.BaseUrl))
            : baseConfig?.BaseUrlPath;
        // compilerOptions.paths replaces the inherited object, including an explicitly empty object.
        var resolvedPathMappings = configDocument.CompilerOptions?.Paths is null && baseConfig is not null
            ? baseConfig.PathMappings.ToDictionary(static mapping => mapping.Pattern, FileSystemPaths.Comparer)
            : new Dictionary<string, PathMapping>(FileSystemPaths.Comparer);

        if (configDocument.CompilerOptions?.Paths is not null)
        {
            foreach (var pathEntry in configDocument.CompilerOptions.Paths)
            {
                if (string.IsNullOrWhiteSpace(pathEntry.Key))
                {
                    continue;
                }

                var targetPatterns = pathEntry.Value
                    .Where(static targetPath => !string.IsNullOrWhiteSpace(targetPath))
                    .ToArray();
                if (targetPatterns.Length == 0)
                {
                    continue;
                }

                resolvedPathMappings[pathEntry.Key] = new PathMapping(
                    pathEntry.Key,
                    targetPatterns,
                    configDirectoryPath);
            }
        }

        visitedConfigPaths.Remove(absoluteConfigPath);

        var resolvedConfig = new ResolvedTsConfig(
            absoluteConfigPath,
            configDirectoryPath,
            baseUrlPath,
            [.. resolvedPathMappings.Values],
            configDocument.CompilerOptions?.Paths is not null || baseConfig?.HasPathMappings == true,
            ResolvePatterns(configDocument.Files, configDirectoryPath) ?? baseConfig?.Files,
            ResolvePatterns(configDocument.Include, configDirectoryPath) ?? baseConfig?.Includes,
            ResolvePatterns(configDocument.Exclude, configDirectoryPath) ?? baseConfig?.Excludes ?? [],
            configDocument.Exclude is not null || baseConfig?.HasExcludes == true,
            configDocument.References?.Select(reference => GetAbsolutePath(fileSystem.Path.Combine(
                configDirectoryPath,
                reference.Path)))
                .ToArray() ?? []);
        if (!string.IsNullOrWhiteSpace(configDocument.CompilerOptions?.OutDir))
        {
            resolvedConfig = resolvedConfig with
            {
                Excludes =
                [
                    .. resolvedConfig.Excludes,
                    GetAbsolutePath(fileSystem.Path.Combine(
                        configDirectoryPath,
                        configDocument.CompilerOptions.OutDir))
                ]
            };
        }
        _configCache[absoluteConfigPath] = resolvedConfig;

        return resolvedConfig;
    }

    private string[]? ResolvePatterns(string[]? patterns, string directory)
        => patterns?.Select(pattern => GetAbsolutePath(fileSystem.Path.Combine(directory, pattern)))
            .ToArray();

    private TsConfigDocument ReadConfig(string configPath)
    {
        if (!fileSystem.File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"The tsconfig file '{configPath}' was not found.",
                configPath);
        }

        var rawContent = fileSystem.File.ReadAllText(configPath);

        return JsonSerializer.Deserialize<TsConfigDocument>(rawContent, _serializerOptions) ?? new TsConfigDocument();
    }

    private string ResolveExtendsPath(
        string configDirectoryPath,
        string extendsPath)
    {
        var candidatePath = ResolveExtendsCandidatePath(configDirectoryPath, extendsPath);

        if (fileSystem.Directory.Exists(candidatePath))
        {
            candidatePath = fileSystem.Path.Combine(candidatePath, "tsconfig.json");
        }
        else if (!candidatePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                 !fileSystem.File.Exists(candidatePath))
        {
            candidatePath += ".json";
        }

        if (!fileSystem.File.Exists(candidatePath))
        {
            throw new FileNotFoundException(
                $"The extended tsconfig file '{candidatePath}' was not found.",
                candidatePath);
        }

        return GetAbsolutePath(candidatePath);
    }

    private string ResolveExtendsCandidatePath(
        string configDirectoryPath,
        string extendsPath)
    {
        if (fileSystem.Path.IsPathRooted(extendsPath))
        {
            return extendsPath;
        }

        if (IsPackageExtendsPath(extendsPath) &&
            TryResolvePackageExtendsPath(configDirectoryPath, extendsPath) is { } packageExtendsPath)
        {
            return packageExtendsPath;
        }

        return fileSystem.Path.Combine(configDirectoryPath, extendsPath);
    }

    private string? TryResolvePackageExtendsPath(
        string configDirectoryPath,
        string extendsPath)
    {
        var currentDirectoryPath = configDirectoryPath;

        while (!string.IsNullOrWhiteSpace(currentDirectoryPath) &&
               repositoryWorkspace.IsSameOrSubPath(currentDirectoryPath))
        {
            var candidatePath = fileSystem.Path.Combine(currentDirectoryPath, "node_modules", extendsPath);
            if (fileSystem.Directory.Exists(candidatePath))
            {
                return fileSystem.Path.Combine(candidatePath, "tsconfig.json");
            }

            if (fileSystem.File.Exists(candidatePath))
            {
                return candidatePath;
            }

            if (!candidatePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var jsonCandidatePath = candidatePath + ".json";
                if (fileSystem.File.Exists(jsonCandidatePath))
                {
                    return jsonCandidatePath;
                }
            }

            if (IsRepositoryRoot(currentDirectoryPath))
            {
                break;
            }

            currentDirectoryPath = fileSystem.DirectoryInfo.New(currentDirectoryPath).Parent?.FullName;
        }

        return null;
    }

    private string? ResolveModulePath(string candidatePath)
    {
        foreach (var moduleCandidate in GetModuleCandidates(candidatePath))
        {
            var absoluteCandidatePath = GetAbsolutePath(moduleCandidate);
            // Missing imported files are dependencies too: creating a formerly unresolved
            // module must refresh its consumers even outside the selected frontend folder.
            _resolutionInputPaths.Add(absoluteCandidatePath);
            if (!fileSystem.File.Exists(absoluteCandidatePath) ||
                !repositoryWorkspace.IsSameOrSubPath(absoluteCandidatePath))
            {
                continue;
            }

            var relativePath = repositoryWorkspace.ToRepositoryRelativePath(absoluteCandidatePath);
            if (!TypeScriptIndexingPathRules.IsIndexedPath(relativePath))
            {
                continue;
            }

            return absoluteCandidatePath;
        }

        return null;
    }

    private string? TryResolveWorkspacePackageImport(string importPath, CancellationToken ct)
    {
        if (!TrySplitPackageImportPath(importPath, out var packageName, out var packageSubpath))
        {
            return null;
        }

        if (!GetWorkspacePackageRootsByName(ct)
            .TryGetValue(packageName, out var packageRootPath))
        {
            return null;
        }

        foreach (var candidatePath in GetWorkspacePackageCandidates(packageRootPath, packageSubpath))
        {
            ct.ThrowIfCancellationRequested();
            var resolvedPath = ResolveModulePath(candidatePath);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        return null;
    }

    private IReadOnlyDictionary<string, string> GetWorkspacePackageRootsByName(CancellationToken ct)
    {
        if (_workspacePackageRootsByName is not null)
        {
            return _workspacePackageRootsByName;
        }

        var packageRootsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendingDirectoryPaths = new Stack<string>();
        pendingDirectoryPaths.Push(GetAbsolutePath(repositoryWorkspace.RootPath));
        var visited = new HashSet<string>(FileSystemPaths.Comparer);

        while (pendingDirectoryPaths.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directoryPath = pendingDirectoryPaths.Pop();
            if (!visited.Add(directoryPath))
            {
                continue;
            }
            var packageManifestPath = fileSystem.Path.Combine(directoryPath, "package.json");
            if (fileSystem.File.Exists(packageManifestPath))
            {
                var packageName = ReadPackageName(packageManifestPath);
                if (!string.IsNullOrWhiteSpace(packageName))
                {
                    packageRootsByName.TryAdd(packageName, directoryPath);
                }
            }

            foreach (var childDirectoryPath in fileSystem.Directory.EnumerateDirectories(directoryPath))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipWorkspacePackageDirectory(childDirectoryPath))
                {
                    continue;
                }

                pendingDirectoryPaths.Push(GetAbsolutePath(childDirectoryPath));
            }
        }

        ct.ThrowIfCancellationRequested();
        _workspacePackageRootsByName = packageRootsByName;

        return _workspacePackageRootsByName;
    }

    private IEnumerable<string> GetWorkspacePackageCandidates(
        string packageRootPath,
        string? packageSubpath)
    {
        var normalizedSubpath = string.IsNullOrWhiteSpace(packageSubpath)
            ? string.Empty
            : packageSubpath;
        var seenPaths = new HashSet<string>(FileSystemPaths.Comparer);

        foreach (var candidatePath in EnumerateWorkspacePackageCandidateRoots(packageRootPath)
            .Select(candidateRootPath =>
                         string.IsNullOrWhiteSpace(normalizedSubpath)
                         ? candidateRootPath
                         : fileSystem.Path.Combine(candidateRootPath, normalizedSubpath)))
        {
            var absoluteCandidatePath = GetAbsolutePath(candidatePath);
            if (seenPaths.Add(absoluteCandidatePath))
            {
                yield return absoluteCandidatePath;
            }
        }
    }

    private IEnumerable<string> EnumerateWorkspacePackageCandidateRoots(string packageRootPath)
    {
        yield return packageRootPath;

        var packageTsConfigPath = fileSystem.Path.Combine(packageRootPath, "tsconfig.json");
        if (fileSystem.File.Exists(packageTsConfigPath))
        {
            var packageBaseUrlPath = TryGetWorkspacePackageBaseUrlPath(packageTsConfigPath);
            if (!string.IsNullOrWhiteSpace(packageBaseUrlPath))
            {
                yield return packageBaseUrlPath;
            }
        }

        yield return fileSystem.Path.Combine(packageRootPath, "src");
    }

    private string? TryGetWorkspacePackageBaseUrlPath(string packageTsConfigPath)
    {
        try
        {
            return LoadConfig(packageTsConfigPath).BaseUrlPath;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private string? ReadPackageName(string packageManifestPath)
    {
        try
        {
            var rawContent = fileSystem.File.ReadAllText(packageManifestPath);

            return JsonSerializer.Deserialize<PackageManifestDocument>(rawContent, _serializerOptions)?.Name;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IEnumerable<string> GetModuleCandidates(string candidatePath)
    {
        var absoluteCandidatePath = GetAbsolutePath(candidatePath);
        var extension = fileSystem.Path.GetExtension(absoluteCandidatePath);
        var seenPaths = new HashSet<string>(FileSystemPaths.Comparer);

        // TypeScript source commonly uses emitted .js specifiers under NodeNext/bundler resolution.
        if (extension.Equals(
            ".js",
            StringComparison.OrdinalIgnoreCase) || extension.Equals(
                ".jsx",
                StringComparison.OrdinalIgnoreCase))
        {
            yield return fileSystem.Path.ChangeExtension(absoluteCandidatePath, ".ts");
            yield return fileSystem.Path.ChangeExtension(absoluteCandidatePath, ".tsx");
            yield return fileSystem.Path.ChangeExtension(absoluteCandidatePath, ".d.ts");
        }

        if (seenPaths.Add(absoluteCandidatePath))
        {
            yield return absoluteCandidatePath;
        }

        if (string.IsNullOrWhiteSpace(extension) ||
            !extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            var tsCandidate = absoluteCandidatePath + ".ts";
            if (seenPaths.Add(tsCandidate))
            {
                yield return tsCandidate;
            }

            var tsxCandidate = absoluteCandidatePath + ".tsx";
            if (seenPaths.Add(tsxCandidate))
            {
                yield return tsxCandidate;
            }

            var declarationCandidate = absoluteCandidatePath + ".d.ts";
            if (seenPaths.Add(declarationCandidate))
            {
                yield return declarationCandidate;
            }
        }

        var indexTsCandidate = fileSystem.Path.Combine(absoluteCandidatePath, "index.ts");
        if (seenPaths.Add(indexTsCandidate))
        {
            yield return indexTsCandidate;
        }

        var indexTsxCandidate = fileSystem.Path.Combine(absoluteCandidatePath, "index.tsx");
        if (seenPaths.Add(indexTsxCandidate))
        {
            yield return indexTsxCandidate;
        }

        var indexDeclarationCandidate = fileSystem.Path.Combine(absoluteCandidatePath, "index.d.ts");
        if (seenPaths.Add(indexDeclarationCandidate))
        {
            yield return indexDeclarationCandidate;
        }
    }

    private string GetAbsolutePath(string path)
        => fileSystem.Path.GetFullPath(path);

    private bool IsRepositoryRoot(string directoryPath)
        => string.Equals(
            GetAbsolutePath(directoryPath),
            GetAbsolutePath(repositoryWorkspace.RootPath),
            FileSystemPaths.Comparison);

    private static bool IsRelativeImport(string importPath)
        => importPath.StartsWith("./", StringComparison.Ordinal) ||
           importPath.StartsWith("../", StringComparison.Ordinal);

    private bool ShouldSkipWorkspacePackageDirectory(string directoryPath)
    {
        var directoryName = fileSystem.Path.GetFileName(directoryPath);

        return string.Equals(directoryName, ".git", FileSystemPaths.Comparison) ||
            string.Equals(directoryName, "node_modules", FileSystemPaths.Comparison) ||
            string.Equals(directoryName, "dist", FileSystemPaths.Comparison) ||
            string.Equals(directoryName, "build", FileSystemPaths.Comparison) ||
            (fileSystem.DirectoryInfo.New(directoryPath).Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool TrySplitPackageImportPath(
        string importPath,
        out string packageName,
        out string? packageSubpath)
    {
        packageName = string.Empty;
        packageSubpath = null;
        if (string.IsNullOrWhiteSpace(importPath))
        {
            return false;
        }

        var pathSegments = importPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
        {
            return false;
        }

        if (importPath[0] == '@')
        {
            if (pathSegments.Length < 2)
            {
                return false;
            }

            packageName = $"{pathSegments[0]}/{pathSegments[1]}";
            packageSubpath = pathSegments.Length > 2
                ? string.Join('/', pathSegments.Skip(2))
                : null;

            return true;
        }

        packageName = pathSegments[0];
        packageSubpath = pathSegments.Length > 1
            ? string.Join('/', pathSegments.Skip(1))
            : null;

        return true;
    }

    private static bool IsPackageExtendsPath(string extendsPath)
        => !extendsPath.StartsWith(".", StringComparison.Ordinal);

    private static IEnumerable<PathMapping> GetOrderedPathMappings(IReadOnlyList<PathMapping> pathMappings)
        => pathMappings
            .OrderByDescending(static mapping => mapping.Pattern.IndexOf('*') < 0)
            .ThenByDescending(static mapping => mapping.Pattern.IndexOf('*'));

    private static bool TryMatchPattern(
        string pattern,
        string importPath,
        out string wildcardValue)
    {
        var firstWildcardIndex = pattern.IndexOf('*');
        if (firstWildcardIndex < 0)
        {
            wildcardValue = string.Empty;

            return string.Equals(pattern, importPath, StringComparison.Ordinal);
        }

        if (pattern.LastIndexOf('*') != firstWildcardIndex)
        {
            wildcardValue = string.Empty;

            return false;
        }

        var prefix = pattern[..firstWildcardIndex];
        var suffix = pattern[(firstWildcardIndex + 1)..];
        if (!importPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !importPath.EndsWith(suffix, StringComparison.Ordinal) ||
            importPath.Length < prefix.Length + suffix.Length)
        {
            wildcardValue = string.Empty;

            return false;
        }

        wildcardValue = importPath[prefix.Length..(importPath.Length - suffix.Length)];

        return true;
    }

    private static string ApplyWildcard(
        string targetPattern,
        string wildcardValue)
        => targetPattern.IndexOf('*') >= 0
            ? targetPattern.Replace("*", wildcardValue, StringComparison.Ordinal)
            : targetPattern;

    private sealed record ResolvedTsConfig(
        string ConfigPath,
        string ConfigDirectoryPath,
        string? BaseUrlPath,
        IReadOnlyList<PathMapping> PathMappings,
        bool HasPathMappings,
        string[]? Files,
        string[]? Includes,
        string[] Excludes,
        bool HasExcludes,
        string[] References);

    private sealed record PathMapping(
        string Pattern,
        IReadOnlyList<string> TargetPatterns,
        string ConfigDirectoryPath);

    private sealed class TsConfigDocument
    {
        public JsonElement Extends { get; init; }

        public string[]? Files { get; init; }
        public string[]? Include { get; init; }
        public string[]? Exclude { get; init; }
        public TsConfigReference[]? References { get; init; }

        public TsConfigCompilerOptions? CompilerOptions { get; init; }
    }

    private sealed class TsConfigCompilerOptions
    {
        public string? BaseUrl { get; init; }

        public Dictionary<string, string[]>? Paths { get; init; }
        public string? OutDir { get; init; }
    }

    private sealed class TsConfigReference
    {
        public string Path { get; init; } = string.Empty;
    }

    private sealed class PackageManifestDocument
    {
        public string? Name { get; init; }
    }
}
