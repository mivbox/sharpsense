using System.Runtime.InteropServices;
using Microsoft.Build.Locator;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Storage;
using TreeSitter;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpSense.Cli.Doctor;

internal static class DoctorChecks
{
    public static IReadOnlyList<IndexDiagnostic> Run(
        WorkspaceSelection workspace,
        LocalEmbeddingsOptions embeddings,
        CancellationToken ct)
    {
        var repositoryRoot = workspace.Workspace.RootPath;
        var checks = new List<IndexDiagnostic>
        {
            new("runtime", "info", $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.RuntimeIdentifier}."),
            Directory.Exists(repositoryRoot)
                ? new("repository", "info", "Repository directory exists.")
                : new("repository-missing", "error", "Repository directory does not exist.", repositoryRoot,
                    "Pass an existing repository directory with --repo-root.")
        };

        var configPath = workspace.ConfigurationPath;
        if (File.Exists(configPath))
        {
            try
            {
                using var reader = File.OpenText(configPath);
                new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build()
                    .Deserialize<WorkspaceDefinition>(reader);
                checks.Add(new("configuration", "info", $"Workspace configuration is readable: {configPath}."));
            }
            catch (Exception exception)
            {
                checks.Add(new("configuration-invalid", "error", $"Could not load workspace configuration: {exception.Message}", configPath,
                    "Correct the workspace configuration before running analyze."));
            }
        }
        else
        {
            checks.Add(new("configuration-missing", "error", "The selected workspace configuration no longer exists.", configPath,
                "Run configure to register the workspace again."));
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            checks.Add(instances.Length > 0
                ? new("msbuild", "info", $"MSBuild SDKs available: {string.Join(", ", instances.Select(instance => instance.Version).Distinct())}.")
                : new("msbuild-missing", "warning", "No MSBuild SDK was found for C# indexing.",
                    Suggestion: "Install the .NET 10 SDK and ensure dotnet is on PATH. TypeScript and Markdown do not require MSBuild."));
        }
        catch (Exception exception)
        {
            checks.Add(new("msbuild-unavailable", "warning", $"Could not discover MSBuild: {exception.Message}",
                Suggestion: "Install the .NET 10 SDK and ensure dotnet is on PATH."));
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            using var typeScript = new Language("TypeScript");
            using var tsx = new Language("TSX");
            using var typeScriptParser = new Parser(typeScript);
            using var tsxParser = new Parser(tsx);
            using var source = typeScriptParser.Parse("export const value: number = 1;");
            using var component = tsxParser.Parse("export const View = () => <div />;");
            if (source is null || component is null || source.RootNode.HasError || component.RootNode.HasError)
            {
                throw new InvalidOperationException("Packaged grammars could not parse valid TypeScript/TSX.");
            }

            checks.Add(new("typescript-runtime", "info", "Native TypeScript and TSX parsers loaded and parsed successfully."));
        }
        catch (Exception exception)
        {
            checks.Add(new("typescript-runtime-unavailable", "error", $"TypeScript runtime check failed: {exception.Message}",
                Suggestion: "Reinstall SharpSense for this operating system and architecture."));
        }

        foreach (var path in new[] { embeddings.ModelPath, embeddings.VocabPath })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.OpenRead(path);
                if (stream.Length == 0)
                {
                    throw new InvalidDataException("The file is empty.");
                }

                checks.Add(new("embedding-asset", "info", $"Embedding asset available: {Path.GetFileName(path)}."));
            }
            catch (Exception exception)
            {
                checks.Add(new("embedding-asset-unavailable", "warning", $"Embedding asset unavailable: {exception.Message}", path,
                    "Reinstall SharpSense to restore the bundled model. Use analyze --no-embeddings for indexing without vector generation."));
            }
        }

        return checks;
    }
}
