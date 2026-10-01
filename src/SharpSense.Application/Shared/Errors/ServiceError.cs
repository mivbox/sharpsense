using FluentResults;

namespace SharpSense.Application.Shared.Errors;

/// <summary>
/// FluentResults <see cref="Error"/> subtype that carries a typed <see cref="ErrorCode"/> from the
/// shared enum. CLI, MCP and HTTP adapters use the code to map expected failures without parsing the message.
/// </summary>
public sealed class ServiceError : Error
{
    public ServiceError(ServiceErrorCode errorCode)
    {
        Message = string.Empty;
        ErrorCode = errorCode;
    }

    public ServiceError(ServiceErrorCode errorCode, string message)
    {
        Message = message;
        ErrorCode = errorCode;
    }

    public ServiceErrorCode ErrorCode { get; }
}
