using Microsoft.CodeAnalysis.MSBuild;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal interface IMsBuildWorkspaceFactory
{
    MSBuildWorkspace Create();
}
