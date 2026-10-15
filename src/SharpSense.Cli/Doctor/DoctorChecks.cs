using Microsoft.Build.Locator;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Storage;
using SharpSense.TypeScript.Parsing;
using System.Runtime.InteropServices;

namespace SharpSense.Cli.Doctor;

internal static class DoctorChecks
{
    public static IReadOnlyList<IndexDiagnostic> Run(
        WorkspaceSelection workspace,
        IWorkspaceCatalog catalog,
        LocalEmbeddingsOptions embeddings,
        CancellationToken ct)
    {
        var repositoryRoot = workspace.Workspace.RootPath;
        var checks = new List<IndexDiagnostic>
        {
            new(
                "runtime",
                "info",
                $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.RuntimeIdentifier}."),
            Directory.Exists(repositoryRoot)
            ? new("repository", "info", "Repository directory exists.")
            : new(
                "repository-missing",
                "error",
                "Repository directory does not exist.",
                repositoryRoot,
                "Pass an existing repository directory with --repo-root.")
        };

        var configPath = workspace.ConfigurationPath;
        if (File.Exists(configPath))
        {
            try
            {
                _ = catalog.ResolveById(workspace.Definition.Id);
                checks.Add(new(
                    "configuration",
                    "info",
                    $"Workspace configuration is readable: {configPath}."));
            }
            catch (Exception exception)
            {
                checks.Add(new(
                    "configuration-invalid",
                    "error",
                    $"Could not load workspace configuration: {exception.Message}",
                    configPath,
                    "Correct the workspace configuration before running analyze."));
            }
        }
        else
        {
            checks.Add(new(
                "configuration-missing",
                "error",
                "The selected workspace configuration no longer exists.",
                configPath,
                "Run configure to register the workspace again."));
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances()
                .ToArray();
            checks.Add(instances.Length > 0
                ? new(
                    "msbuild",
                    "info",
                    $"MSBuild SDKs available: {string.Join(
                        ", ",
                        instances
                            .Select(instance => instance.Version)
                            .Distinct())}.")
                : new(
                    "msbuild-missing",
                    "warning",
                    "No MSBuild SDK was found for C# indexing.",
                    Suggestion: "Install the .NET 10 SDK and ensure dotnet is on PATH. TypeScript and Markdown do not require MSBuild."));
        }
        catch (Exception exception)
        {
            checks.Add(new(
                "msbuild-unavailable",
                "warning",
                $"Could not discover MSBuild: {exception.Message}",
                Suggestion: "Install the .NET 10 SDK and ensure dotnet is on PATH."));
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var source = TypeScriptParser.Parse(
                "check.ts",
                """
                export type * from './model';
                export type Producer<out T> = () => T;
                export interface Check { run: (string) => boolean; }
                mock<typeof import('library')>('library', () => ({}),);
                """,
                ct);
            var component = TypeScriptParser.Parse("check.tsx", "export const View = () => <div>A & B &amp; C</div>;", ct);
            if (source.HasErrors || component.HasErrors)
            {
                throw new InvalidOperationException("The managed parser could not parse valid TypeScript/TSX.");
            }

            checks.Add(new(
                "typescript-runtime",
                "info",
                "Managed TypeScript and TSX parsing passed compatibility checks."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            checks.Add(new(
                "typescript-runtime-unavailable",
                "error",
                $"TypeScript runtime check failed: {exception.Message}",
                Suggestion: "Reinstall SharpSense to restore the managed TypeScript parser."));
        }

        foreach (var path in new[]
        {
            embeddings.ModelPath,
            embeddings.VocabPath
        })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.OpenRead(path);
                if (stream.Length == 0)
                {
                    throw new InvalidDataException("The file is empty.");
                }

                checks.Add(new(
                    "embedding-asset",
                    "info",
                    $"Embedding asset available: {Path.GetFileName(path)}."));
            }
            catch (Exception exception)
            {
                checks.Add(new(
                    "embedding-asset-unavailable",
                    "warning",
                    $"Embedding asset unavailable: {exception.Message}",
                    path,
                    "Reinstall SharpSense to restore the bundled model. Use analyze --no-embeddings for indexing without vector generation."));
            }
        }

        return checks;
    }
}
