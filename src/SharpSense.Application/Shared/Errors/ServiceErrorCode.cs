namespace SharpSense.Application.Shared.Errors;

public enum ServiceErrorCode
{
    InvalidArgument = 400,
    PermissionDenied = 403,
    NotFound = 404,
    Conflict = 409,
    Gone = 410,
    PreconditionRequired = 428,
    FailedPrecondition = 412,
    UnprocessableContent = 422,
    ResourceExhausted = 429,
    InternalError = 500,
    ThirdPartyError = 502,
}
