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

    public MSBuildWorkspace Create()
    {
        MsBuildLocatorRegistration.EnsureRegistered();

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
            ["DesignCheck"] = "false",
            ["CheckForSystemRuntimeDependency"] = "true"
        };
        var workspace = MSBuildWorkspace.Create(properties, _hostServices.Value);

        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = true;

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
