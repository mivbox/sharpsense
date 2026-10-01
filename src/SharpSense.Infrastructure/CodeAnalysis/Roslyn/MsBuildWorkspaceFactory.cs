using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class MsBuildWorkspaceFactory : IMsBuildWorkspaceFactory
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
            ["CheckForSystemRuntimeDependency"] = "true",
            // Analysis can use partial semantic models. Keep warnings (including NuGet
            // audit findings) visible without promoting them to project-load failures.
            ["TreatWarningsAsErrors"] = "false",
            ["WarningsAsErrors"] = string.Empty,
            ["MSBuildWarningsAsErrors"] = string.Empty
        };
        var workspace = MSBuildWorkspace.Create(properties, _hostServices.Value);

        workspace.SkipUnrecognizedProjects = true;
        // Graph relationships require source-project identities even when referenced DLLs exist.
        workspace.LoadMetadataForReferencedProjects = false;

        return workspace;
    }

    private static MefHostServices CreateHostServices()
    {
        var assemblies = MefHostServices.DefaultAssemblies
            .Add(typeof(MSBuildWorkspace).Assembly)
            .Add(typeof(CSharpFormattingOptions).Assembly);

        return MefHostServices.Create(assemblies.Distinct());
    }
}
