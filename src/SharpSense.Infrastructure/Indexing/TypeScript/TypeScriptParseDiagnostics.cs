using FluentResults;
using SharpSense.Application.Shared.Errors;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptParseDiagnostics
{
    private const int MaximumFileErrors = 49;
    private readonly List<IError> _errors = [];
    private int _failedFiles;

    public bool HasErrors => _failedFiles > 0;

    public void Add(string path, ParseDiagnostic diagnostic)
    {
        _failedFiles++;
        if (_errors.Count >= MaximumFileErrors)
        {
            return;
        }

        var error = new ServiceError(
            ServiceErrorCode.ThirdPartyError,
            $"TypeScript parser error in '{path}' at {diagnostic.Line}:{diagnostic.Column}: " +
            $"TS{diagnostic.Code}: {diagnostic.Message} The source may contain invalid or unsupported syntax.");
        error.Metadata["filePath"] = path;
        error.Metadata["line"] = diagnostic.Line;
        error.Metadata["column"] = diagnostic.Column;
        error.Metadata["diagnosticCode"] = diagnostic.Code;
        _errors.Add(error);
    }

    public IEnumerable<IError> GetErrors()
    {
        var omitted = _failedFiles - _errors.Count;
        yield return new ServiceError(
            ServiceErrorCode.ThirdPartyError,
            $"TypeScript parsing failed for {_failedFiles} file(s); previous index preserved. " +
            (omitted > 0 ? $"Showing {_errors.Count} file diagnostics; {omitted} omitted." : "All file diagnostics follow."));

        foreach (var error in _errors)
        {
            yield return error;
        }
    }
}
