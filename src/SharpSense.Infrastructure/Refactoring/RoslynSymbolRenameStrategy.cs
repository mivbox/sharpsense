using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Refactoring;

internal sealed class RoslynSymbolRenameStrategy(
    IWorkspaceLoader workspaceLoader,
    WorkspaceTargetResolver targetResolver,
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
    : IRenameStrategy
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WorkspaceGates = new(GetPathComparer());

    public bool CanHandle(DocumentKind kind)
        => kind == DocumentKind.Source;

    public async Task<RefactorResult> RenameAsync(
        NodeRefactorTarget target,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(newName);

        var targetResolution = ResolveWorkspaceTarget(
            targetPath,
            target.RelativeProjectPath);
        if (!targetResolution.Success)
        {
            return Failure(targetResolution.ErrorMessage);
        }

        var normalizedWorkspaceTargetPath = fileSystem.Path.GetFullPath(targetResolution.TargetPath);
        var workspaceGate = WorkspaceGates.GetOrAdd(
            normalizedWorkspaceTargetPath,
            static _ => new SemaphoreSlim(1, 1));
        await workspaceGate.WaitAsync(ct);

        try
        {
            var loadResult = await workspaceLoader.Load(
                targetResolution.TargetPath,
                ct: ct);
            if (loadResult.IsFailed)
            {
                var combinedMessage = string.Join(
                    Environment.NewLine,
                    loadResult.Errors.Select(static error => $"{error.GetType().Name}: {error.Message}"));
                return Failure(
                    $"Workspace load failed for '{normalizedWorkspaceTargetPath}': {combinedMessage}");
            }

            var solution = loadResult.Value.Solution;
            var workspace = solution.Workspace;
            var absoluteFilePath = fileSystem.Path.GetFullPath(
                fileSystem.Path.Combine(
                    repositoryWorkspace.RootPath,
                    target.RelativeFilePath));
            var document = FindDocument(
                solution,
                absoluteFilePath);
            if (document is null)
            {
                return Failure($"Unable to locate document '{absoluteFilePath}' in workspace '{normalizedWorkspaceTargetPath}'.");
            }

            var sourceText = await document.GetTextAsync(ct);
            var startPositionResolution = ResolveStartPosition(
                sourceText,
                target);
            if (!startPositionResolution.Success)
            {
                return Failure(startPositionResolution.ErrorMessage);
            }

            var syntaxRoot = await document.GetSyntaxRootAsync(ct);
            if (syntaxRoot is null)
            {
                return Failure($"Unable to load the syntax root for '{target.RelativeFilePath}'.");
            }

            var semanticModel = await document.GetSemanticModelAsync(ct);
            if (semanticModel is null)
            {
                return Failure($"Unable to load the semantic model for '{target.RelativeFilePath}'.");
            }

            var symbol = ResolveDeclaredSymbol(
                syntaxRoot,
                semanticModel,
                startPositionResolution.StartPosition,
                ct);
            if (symbol is null)
            {
                return Failure($"Unable to resolve a declared symbol for node id {target.NodeId} at '{target.RelativeFilePath}:{target.StartLine}'.");
            }

            var renameOptions = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);
            var renamedSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                renameOptions,
                newName,
                ct);
            var modifiedFiles = GetModifiedFilePaths(
                solution,
                renamedSolution);

            if (!workspace.TryApplyChanges(renamedSolution))
            {
                return Failure($"Roslyn could not apply rename changes for '{symbol.Name}'.");
            }

            if (modifiedFiles.Length > 0)
            {
                await workspaceLoader.UpdateDocuments(
                    targetResolution.TargetPath,
                    [.. modifiedFiles.Select(path => new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        path,
                        path))],
                    ct);
            }

            return new RefactorResult(
                true,
                [.. modifiedFiles.Select(repositoryWorkspace.ToRepositoryRelativePath)],
                string.Empty);
        }
        finally
        {
            workspaceGate.Release();
        }
    }

    private TargetPathResolution ResolveWorkspaceTarget(
        string? explicitTargetPath,
        string? relativeProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitTargetPath))
        {
            return targetResolver.Resolve(explicitTargetPath);
        }

        var discoveredTarget = targetResolver.Resolve(null);
        if (discoveredTarget.Success)
        {
            return discoveredTarget;
        }

        return !string.IsNullOrWhiteSpace(relativeProjectPath)
            ? targetResolver.Resolve(relativeProjectPath)
            : discoveredTarget;
    }

    private Document? FindDocument(
        Solution solution,
        string absoluteFilePath)
    {
        var pathComparer = GetPathComparer();

        return solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(document => document.FilePath is not null &&
                                       pathComparer.Equals(fileSystem.Path.GetFullPath(document.FilePath), absoluteFilePath));
    }

    private static StartPositionResolution ResolveStartPosition(
        SourceText sourceText,
        NodeRefactorTarget target)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(target);

        if (target.StartLine <= 0)
        {
            return FailureStartPosition($"Persisted start line '{target.StartLine}' must be greater than zero.");
        }

        var lines = sourceText.Lines;
        if (target.StartLine > lines.Count)
        {
            return FailureStartPosition(
                $"Persisted start line '{target.StartLine}' falls outside '{target.RelativeFilePath}', which has {lines.Count} line(s).");
        }

        return new StartPositionResolution(
            true,
            lines[target.StartLine - 1].Start,
            string.Empty);
    }

    private static ISymbol? ResolveDeclaredSymbol(
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        int startPosition,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(syntaxRoot);
        ArgumentNullException.ThrowIfNull(semanticModel);

        var currentNode = syntaxRoot.FindNode(
            new TextSpan(startPosition, 0),
            getInnermostNodeForTie: true);
        while (currentNode is not null)
        {
            var symbol = semanticModel.GetDeclaredSymbol(
                currentNode,
                ct);
            if (symbol is not null)
            {
                return symbol;
            }

            currentNode = currentNode.Parent;
        }

        return null;
    }

    private string[] GetModifiedFilePaths(
        Solution oldSolution,
        Solution newSolution)
    {
        ArgumentNullException.ThrowIfNull(oldSolution);
        ArgumentNullException.ThrowIfNull(newSolution);

        var pathComparer = GetPathComparer();
        var changedFilePaths = new HashSet<string>(pathComparer);
        var solutionChanges = newSolution.GetChanges(oldSolution);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = newSolution.GetDocument(documentId);
                if (!string.IsNullOrWhiteSpace(document?.FilePath))
                {
                    changedFilePaths.Add(fileSystem.Path.GetFullPath(document.FilePath));
                }
            }
        }

        return [.. changedFilePaths.OrderBy(static path => path, pathComparer)];
    }

    private static RefactorResult Failure(string errorMessage)
        => new(
            false,
            [],
            errorMessage);

    private static StartPositionResolution FailureStartPosition(string errorMessage)
        => new(
            false,
            0,
            errorMessage);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly record struct StartPositionResolution(
        bool Success,
        int StartPosition,
        string ErrorMessage);
}
