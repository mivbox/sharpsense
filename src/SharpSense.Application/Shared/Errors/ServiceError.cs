using FluentResults;

namespace SharpSense.Application.Shared.Errors;

/// <summary>
/// FluentResults <see cref="Error"/> subtype that carries a typed <see cref="ErrorCode"/> from the
/// shared enum. Use this instead of the plain <see cref="Error"/> base whenever the failure mode
/// has a canonical code so the CLI, MCP, and any future HTTP transport can map the failure to a
/// uniform exit status without re-parsing free-form messages.
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

    public ServiceErrorCode ErrorCode
    {
        get;
    }
}
