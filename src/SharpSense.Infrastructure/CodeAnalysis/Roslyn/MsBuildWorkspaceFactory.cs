using System.Reflection;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public sealed class MsBuildWorkspaceFactory : IMsBuildWorkspaceFactory
{
    private static readonly Lazy<HostServices> _hostServices = new(
        CreateHostServices,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public MSBuildWorkspace Create(RoslynWorkspaceOptions? options = null)
    {
        MsBuildLocatorRegistration.EnsureRegistered();

        var effectiveOptions = options ?? new RoslynWorkspaceOptions();
        var properties = effectiveOptions.MSBuildProperties.Count == 0
            ? null
            : effectiveOptions.MSBuildProperties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var workspace = properties is null
            ? MSBuildWorkspace.Create(_hostServices.Value)
            : MSBuildWorkspace.Create(properties, _hostServices.Value);

        workspace.SkipUnrecognizedProjects = effectiveOptions.SkipUnrecognizedProjects;
        workspace.LoadMetadataForReferencedProjects = effectiveOptions.LoadMetadataForReferencedProjects;

        return workspace;
    }

    private static MefHostServices CreateHostServices()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Add(typeof(MSBuildWorkspace).Assembly)
            .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"));

        return MefHostServices.Create(assemblies.Distinct());
    }
}
