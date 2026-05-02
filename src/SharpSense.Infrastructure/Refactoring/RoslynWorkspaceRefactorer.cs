using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Refactoring;

internal sealed class RoslynWorkspaceRefactorer(
    IWorkspaceLoader workspaceLoader,
    WorkspaceTargetResolver targetResolver,
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
    : IWorkspaceRefactorer
{
    public async Task<RefactorResult> RefactorNode(
        RefactorTarget target,
        string newSourceCode,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(newSourceCode);

        var targetResolution = targetResolver.Resolve(targetPath);
        if (!targetResolution.Success)
        {
            return Failure(targetResolution.ErrorMessage);
        }

        var normalizedSourceCode = NormalizeReplacementSource(newSourceCode);
        var absoluteFilePath = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(
                repositoryWorkspace.RootPath,
                target.RelativeFilePath));
        var updateResult = await workspaceLoader.ChangeDocumentText(
            targetResolution.TargetPath,
            absoluteFilePath,
            sourceText =>
            {
                var textSpanResolution = ResolveTextSpan(sourceText, target);
                if (!textSpanResolution.Success)
                {
                    return WorkspaceTextChange.Failure(textSpanResolution.ErrorMessage);
                }

                return WorkspaceTextChange.SuccessChange(
                    sourceText.WithChanges(
                        new TextChange(
                            textSpanResolution.TextSpan,
                            normalizedSourceCode)));
            },
            ct);
        if (!updateResult.Success)
        {
            return Failure(updateResult.ErrorMessage);
        }

        return new RefactorResult(
            true,
            [repositoryWorkspace.ToRepositoryRelativePath(absoluteFilePath)],
            string.Empty);
    }

    private static TextSpanResolution ResolveTextSpan(
        SourceText sourceText,
        RefactorTarget target)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(target);

        if (target.StartLine <= 0)
        {
            return TextSpanFailure($"Persisted start line '{target.StartLine}' must be greater than zero.");
        }

        if (target.EndLine < target.StartLine)
        {
            return TextSpanFailure($"Persisted line span '{target.StartLine}-{target.EndLine}' is invalid.");
        }

        var lines = sourceText.Lines;
        if (target.StartLine > lines.Count || target.EndLine > lines.Count)
        {
            return TextSpanFailure(
                $"Persisted line span '{target.StartLine}-{target.EndLine}' falls outside '{target.RelativeFilePath}', which has {lines.Count} line(s).");
        }

        var startLineIndex = target.StartLine - 1;
        var endLineIndex = target.EndLine - 1;
        var startPosition = lines[startLineIndex].Start;
        var endPosition = lines[endLineIndex].End;

        return new TextSpanResolution(
            true,
            TextSpan.FromBounds(startPosition, endPosition),
            string.Empty);
    }

    private static RefactorResult Failure(string errorMessage)
        => new(
            false,
            [],
            errorMessage);

    private static string NormalizeReplacementSource(string newSourceCode)
    {
        ArgumentNullException.ThrowIfNull(newSourceCode);

        return newSourceCode.TrimEnd('\r', '\n');
    }

    private static TextSpanResolution TextSpanFailure(string errorMessage)
        => new(
            false,
            default,
            errorMessage);

    private readonly record struct TextSpanResolution(
        bool Success,
        TextSpan TextSpan,
        string ErrorMessage);
}
