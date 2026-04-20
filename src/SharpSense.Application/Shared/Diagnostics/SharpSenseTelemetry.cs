using Serilog;
using SerilogTimings;
using SerilogTimings.Extensions;
using System.Diagnostics;

namespace SharpSense.Application.Shared.Diagnostics;

public class SharpSenseTraceSpan : IDisposable
{
    private static readonly ILogger _log = Log.ForContext<SharpSenseTraceSpan>();

    public static string ActivitySourceName => "SharpSense.Observability";

    private static readonly ActivitySource _activitySource = new(name: ActivitySourceName);

    private readonly Activity? _activity;
    private readonly Operation? _operation;
    private bool _operationAbandoned;


    public static SharpSenseTraceSpan Start(string name) => new(name, parentId: null);
    public static SharpSenseTraceSpan Start(string name, string? parentId) => new(name, parentId);

    public static SharpSenseTraceSpan Start(string name, string? parentId, ILogger operationLogger) =>
        new(name, parentId, operationLogger);

    private SharpSenseTraceSpan(string name, string? parentId, ILogger? log = null)
    {
        _activity = _activitySource.StartActivity(name, ActivityKind.Internal, parentId);
        var operationLogger = log ?? _log;
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            operationLogger = operationLogger.ForContext("SharpSenseTraceSpanParentId", parentId);
        }

        _operation = operationLogger.BeginOperation("Traced {OperationName} operation", name);
    }

    public void AddTag(string key, object value)
    {
        _operation?.EnrichWith(key, value, destructureObjects: false);
        _activity?.AddTag(key, value);
    }

    /// <summary>
    /// Records this span as having errored, without setting any exception/reason.
    /// Call <see cref="RecordExceptionAndErrorStatus"/> instead if you have an exception
    /// that provides any additional information.
    /// </summary>
    public void SetError()
    {
        _activity?.SetStatus(ActivityStatusCode.Error);
        _operation?.Abandon();
    }

    public void RecordExceptionAndErrorStatus(Exception ex)
    {
        _activity?.SetStatus(ActivityStatusCode.Error);
        //_activity?.AddException(ex);
        if (_operation != null)
        {
            _operation.Abandon(ex);
            _operationAbandoned = true;
        }
    }

    public void Dispose()
    {
        if (_operation != null && !_operationAbandoned)
        {
            _operation.Complete();
        }
        _operation?.Dispose();
        _activity?.Dispose();
    }
}
