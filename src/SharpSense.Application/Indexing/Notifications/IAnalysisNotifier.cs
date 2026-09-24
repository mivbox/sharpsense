namespace SharpSense.Application.Indexing.Notifications;

/// <summary>
/// Receives ordered observations of analysis. Implementations must update bounded local
/// state without waiting for rendering, network clients, or other external consumers.
/// </summary>
public interface IAnalysisNotifier
{
    void Notify(AnalysisNotification notification);
}
