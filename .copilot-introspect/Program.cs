using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;

MsBuildLocatorRegistration.EnsureRegistered();
var assemblies = MefHostServices.DefaultAssemblies
    .Add(typeof(MSBuildWorkspace).Assembly)
    .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"));
var host = MefHostServices.Create(assemblies);
using var workspace = MSBuildWorkspace.Create(host);
workspace.SkipUnrecognizedProjects = true;
workspace.WorkspaceFailed += (_, args) => Console.WriteLine($"DIAG {args.Diagnostic}");
var solution = await workspace.OpenSolutionAsync("/Users/mitch.box/sources/7Twenty/sharp-sense/SharpSense.sln");
Console.WriteLine($"Projects={solution.Projects.Count()}");
foreach (var project in solution.Projects)
{
    Console.WriteLine(project.Name + " => " + project.FilePath);
}
