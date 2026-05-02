namespace SharpSense.Application.Refactoring.Models;

public sealed record RefactorTarget(
    int NodeId,
    string RelativeFilePath,
    int StartLine,
    int EndLine);
