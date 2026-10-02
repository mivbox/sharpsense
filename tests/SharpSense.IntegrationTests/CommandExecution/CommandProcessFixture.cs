using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace SharpSense.IntegrationTests.CommandExecution;

public sealed class CommandProcessFixture : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("sharpsense-command-fixture-");

    public string Executable { get; }

    public CommandProcessFixture()
    {
        Executable = Path.Combine(_directory.FullName, "CommandFixture.dll");
        const string source = """
            using System;
            using System.Diagnostics;
            using System.IO;
            using System.Threading.Tasks;

            if (args[0] == "exit")
            {
                Console.WriteLine("command output");
                Console.Error.WriteLine("command error");
                return int.Parse(args[1]);
            }
            if (args[0] == "marker")
            {
                File.WriteAllText(args[1], "started");
                return 0;
            }
            if (args[0] == "parent")
            {
                Console.WriteLine("parent:" + Environment.ProcessId);
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
                start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
                start.ArgumentList.Add("block");
                using var child = Process.Start(start);
                return 0;
            }
            Console.WriteLine("ready:" + Environment.ProcessId);
            await Task.Delay(-1);
            return 0;
            """;
        var platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(
            "CommandFixture",
            [CSharpSyntaxTree.ParseText(source)],
            platformAssemblies.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var result = compilation.Emit(Executable);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        File.WriteAllText(Path.ChangeExtension(Executable, ".runtimeconfig.json"), JsonSerializer.Serialize(new
        {
            runtimeOptions = new
            {
                tfm = "net10.0",
                framework = new { name = "Microsoft.NETCore.App", version = "10.0.0" }
            }
        }));
    }

    public string Command(string arguments) => $"dotnet \"{Executable}\" {arguments}";

    public void Dispose() => _directory.Delete(recursive: true);
}
