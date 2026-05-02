namespace SharpSense.Application.Refactoring.Models;

public sealed record RefactorResult(
    bool Success,
    string[] ModifiedFilePaths,
    string ErrorMessage);
