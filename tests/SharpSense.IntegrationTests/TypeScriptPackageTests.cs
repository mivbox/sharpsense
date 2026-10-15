using AwesomeAssertions;
using SharpSense.Testkit;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace SharpSense.IntegrationTests;

public sealed class TypeScriptPackageTests
{
    [Fact]
    public async Task WhenInstalledPackageAnalysesTypeScript_ThenEmbedsAndPreservesGraphAcrossParseFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("sharpsense-package-");
        try
        {
            var source = Path.Combine(directory.FullName, "source");
            var home = Path.Combine(directory.FullName, "home");
            var packages = Path.Combine(directory.FullName, "packages");
            var tool = Path.Combine(directory.FullName, "tool");
            Directory.CreateDirectory(source);
            var version = "0.0.0-smoke." + Guid.NewGuid().ToString("N");
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            List<string> packArguments =
            [
                "pack", "src/SharpSense.Cli/SharpSense.Cli.csproj", "--no-build", "-c", configuration,
                $"-p:Version={version}", "-o", packages
            ];

            await Run("dotnet", RepositoryPaths.Root, [.. packArguments]);
            var nugetConfig = Path.Combine(directory.FullName, "NuGet.Config");
            new XDocument(new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "smoke"), new XAttribute("value", packages)))))
                .Save(nugetConfig);
            await Run(
                "dotnet",
                source,
                ["tool", "install", "SharpSense.Cli", "--version", version, "--configfile", nugetConfig, "--tool-path", tool],
                isolateNuGet: true);
            var executable = Path.Combine(tool, OperatingSystem.IsWindows() ? "sharpsense.exe" : "sharpsense");
            var files = new Dictionary<string, string>
            {
                ["tsconfig.json"] = """{"include":["*.ts","*.tsx"]}""",
                ["model.ts"] = "export interface Model<out T> { value: T; check: (string) => boolean; }",
                ["barrel.ts"] = "export type * from './model'; export type * as models from './model';",
                ["view.tsx"] = "import type { Model } from './barrel'; export const View = () => <div>😀 A & B &amp; C</div>;",
                ["api.ts"] = "export function load() { return axios.get<typeof import('./model')>('/orders', {},); }"
            };
            foreach (var (name, content) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(source, name), content, ct);
            }

            await Run(
                executable,
                source,
                ["workspace", "create", "parser-smoke", "--repo-root", source, "--typescript", "tsconfig.json", "--json"]);
            await Run(executable, source, ["analyze", "--workspace", "parser-smoke"]);

            var report = await Doctor();
            report
                .GetProperty("checks")
                .EnumerateArray()
                .Should()
                .Contain(check => check.GetProperty("code").GetString() == "typescript-runtime");
            var graph = report.GetProperty("graph");
            graph.GetProperty("isIndexed").GetBoolean().Should().BeTrue();
            graph.GetProperty("codeNodeCount").GetInt32().Should().Be(5);
            graph.GetProperty("embeddedNodeCount").GetInt32().Should().Be(5);
            graph.GetProperty("edgeCount").GetInt32().Should().BeGreaterThan(0);

            await File.WriteAllTextAsync(Path.Combine(source, "model.ts"), "export interface Model {", ct);
            var failure = await Run(executable, source, ["analyze", "--workspace", "parser-smoke"], expectedExit: 1);

            failure.Should().Contain("model.ts").And.Contain("previous index preserved");
            var failed = (await Doctor(expectedExit: 1)).GetProperty("graph");
            failed
                .GetProperty("lastSuccessfulIndex")
                .GetRawText()
                .Should()
                .Be(graph.GetProperty("lastSuccessfulIndex").GetRawText());
            failed.GetProperty("codeNodeCount").GetInt32().Should().Be(5);

            await File.WriteAllTextAsync(Path.Combine(source, "model.ts"), files["model.ts"], ct);
            await Run(executable, source, ["analyze", "--workspace", "parser-smoke"]);
            var recovered = (await Doctor()).GetProperty("graph");
            recovered.GetProperty("embeddedNodeCount").GetInt32().Should().Be(5);

            async Task<JsonElement> Doctor(int expectedExit = 0)
            {
                var output = await Run(executable, source, ["doctor", "--workspace", "parser-smoke", "--json"], expectedExit);
                using var document = JsonDocument.Parse(output);

                return document.RootElement.Clone();
            }

            async Task<string> Run(
                string command,
                string workingDirectory,
                string[] arguments,
                int expectedExit = 0,
                bool isolateNuGet = false)
            {
                var start = new ProcessStartInfo(command)
                {
                    WorkingDirectory = workingDirectory
                };
                start.Environment["SHARPSENSE_HOME"] = home;
                start.Environment["DOTNET_NOLOGO"] = "true";
                if (isolateNuGet)
                {
                    start.Environment["NUGET_PACKAGES"] = Path.Combine(directory.FullName, "nuget");
                }

                foreach (var argument in arguments)
                {
                    start.ArgumentList.Add(argument);
                }

                var result = await TestProcess.Run(start, ct);
                result.ExitCode.Should().Be(expectedExit, result.Output + result.Error);

                return result.Output;
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
