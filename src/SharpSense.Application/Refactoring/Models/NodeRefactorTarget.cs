namespace SharpSense.Application.Refactoring.Models;

public sealed record NodeRefactorTarget(
    int NodeId,
    string RelativeFilePath,
    int StartLine,
    int EndLine,
    string? RelativeProjectPath = null,
    DocumentKind DocumentKind = DocumentKind.Source);
