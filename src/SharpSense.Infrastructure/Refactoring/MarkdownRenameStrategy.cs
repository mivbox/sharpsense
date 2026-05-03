using Microsoft.CodeAnalysis.Text;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Text.RegularExpressions;

namespace SharpSense.Infrastructure.Refactoring;

internal sealed partial class MarkdownRenameStrategy(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
    : IRenameStrategy
{
    public bool CanHandle(DocumentKind kind)
        => kind == DocumentKind.Markdown;

    public async Task<RefactorResult> RenameAsync(
        NodeRefactorTarget target,
        string newName,
        string? targetPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(newName);

        var absoluteFilePath = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(
                repositoryWorkspace.RootPath,
                target.RelativeFilePath));
        if (!fileSystem.File.Exists(absoluteFilePath))
        {
            return Failure($"Markdown file '{absoluteFilePath}' was not found.");
        }

        var rawText = await fileSystem.File.ReadAllTextAsync(absoluteFilePath, ct);
        var sourceText = SourceText.From(rawText);
        var textSpanResolution = RefactorTextSpanResolver.Resolve(sourceText, target);
        if (!textSpanResolution.Success)
        {
            return Failure(textSpanResolution.ErrorMessage);
        }

        var currentText = sourceText.ToString(textSpanResolution.TextSpan);
        var renamedText = RenameHeadingText(
            currentText,
            newName);
        if (string.Equals(currentText, renamedText, StringComparison.Ordinal))
        {
            return Failure($"Unable to locate a Markdown heading to rename within '{target.RelativeFilePath}:{target.StartLine}-{target.EndLine}'.");
        }

        var updatedText = sourceText.WithChanges(
            new TextChange(
                textSpanResolution.TextSpan,
                renamedText.TrimEnd('\r', '\n')));
        await fileSystem.File.WriteAllTextAsync(
            absoluteFilePath,
            updatedText.ToString(),
            ct);

        return new RefactorResult(
            true,
            [repositoryWorkspace.ToRepositoryRelativePath(absoluteFilePath)],
            string.Empty);
    }

    private static string RenameHeadingText(string currentText, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var match = MarkdownHeadingPattern().Match(currentText);
        if (!match.Success)
        {
            return currentText;
        }

        var nameGroup = match.Groups["name"];

        return string.Concat(
            currentText.AsSpan(0, nameGroup.Index),
            newName,
            currentText.AsSpan(nameGroup.Index + nameGroup.Length)
        );
    }

    private static RefactorResult Failure(string errorMessage)
        => new(
            false,
            [],
            errorMessage);

    [GeneratedRegex(@"^(?<prefix>\s*#{1,6}\s+)(?<name>.*?)(?<suffix>\s*)$", RegexOptions.Multiline)]
    private static partial Regex MarkdownHeadingPattern();
}
