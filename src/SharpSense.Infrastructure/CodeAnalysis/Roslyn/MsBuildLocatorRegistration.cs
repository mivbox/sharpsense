using Microsoft.Build.Locator;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public static class MsBuildLocatorRegistration
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void EnsureRegistered(Func<IEnumerable<VisualStudioInstance>>? instanceQuery = null)
    {
        lock (SyncRoot)
        {
            if (_initialized || MSBuildLocator.IsRegistered)
            {
                _initialized = true;
                return;
            }

            if (!MSBuildLocator.CanRegister)
            {
                throw new InvalidOperationException("MSBuildLocator must register before any MSBuild assemblies are loaded.");
            }

            var selectedInstance = SelectPreferredVisualStudioInstance(instanceQuery?.Invoke() ?? MSBuildLocator.QueryVisualStudioInstances());
            MSBuildLocator.RegisterInstance(selectedInstance);
            _initialized = true;
        }
    }

    public static MsBuildInstanceCandidate SelectPreferredInstance(IEnumerable<MsBuildInstanceCandidate> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);

        return instances
            .OrderByDescending(static instance => instance.Version)
            .ThenBy(static instance => instance.Name, StringComparer.Ordinal)
            .ThenBy(static instance => instance.DiscoveryType)
            .ThenBy(static instance => instance.MSBuildPath, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No MSBuild instances were discovered.");
    }

    private static VisualStudioInstance SelectPreferredVisualStudioInstance(IEnumerable<VisualStudioInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);

        return instances
            .OrderByDescending(static instance => instance.Version)
            .ThenBy(static instance => instance.Name, StringComparer.Ordinal)
            .ThenBy(static instance => instance.DiscoveryType)
            .ThenBy(static instance => instance.MSBuildPath, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No MSBuild instances were discovered.");
    }
}
