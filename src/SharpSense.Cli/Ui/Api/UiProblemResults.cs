using FluentResults;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Cli.Ui.Api;

internal static class UiProblemResults
{
    public static IResult Failure(IEnumerable<IError> errors, int defaultStatus = 400)
    {
        var all = errors.ToArray();
        var status = all.OfType<ServiceError>()
            .Select(error => (int)error.ErrorCode)
            .FirstOrDefault(defaultStatus);

        return Results.Problem(
            statusCode: status,
            title: "Request failed",
            detail: string.Join(
                "; ",
                all.Select(error => error.Message)));
    }

    public static IResult Invalid(string detail) => Results.Problem(
        statusCode: 400,
        title: "Invalid request",
        detail: detail);

    public static IResult MissingNode(int nodeId) => Results.Problem(
        statusCode: 404,
        title: "Node not found",
        detail: $"No indexed code node exists for id {nodeId}.");
}
