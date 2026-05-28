using Microsoft.CodeAnalysis.MSBuild;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

public interface IMsBuildWorkspaceFactory
{
    MSBuildWorkspace Create()
        => Create(options: null);

    MSBuildWorkspace Create(RoslynWorkspaceOptions? options = null);
}
