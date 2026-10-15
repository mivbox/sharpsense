#:property PublishAot=false
#:property UseAppHost=false

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SharpSense.Tools;

internal static partial class UpdateTypeScriptCorpus
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly CorpusJsonContext _jsonContext = new(_jsonOptions);

    public static async Task<int> Main(string[] args)
    {
        var check = args.Contains("--check", StringComparer.Ordinal);
        var paths = args
            .Where(argument => argument != "--check")
            .ToArray();
        if (paths.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("From the repository root: dotnet run tools/UpdateTypeScriptCorpus.cs -- <typescript.js> [source-checkout] [--check]");

            return 1;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            await Update(Path.GetFullPath(paths[0]), paths.ElementAtOrDefault(1), check, cancellation.Token);

            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static async Task Update(string compilerPath, string? checkout, bool check, CancellationToken ct)
    {
        var directory = Path.GetFullPath("tests/SharpSense.TypeScript.Tests/Compatibility");
        var manifest = JsonSerializer.Deserialize<Manifest>(
            await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"), ct),
            _jsonContext.Manifest)!;
        if (!Regex.IsMatch(manifest.SourceRevision, "^[a-f0-9]{40}$"))
        {
            throw new InvalidDataException("The corpus must pin a full upstream commit hash.");
        }

        using var client = new HttpClient();
        var cases = new List<SourceCase>();
        foreach (var path in manifest.Files)
        {
            var bytes = checkout is null
                ? await client.GetByteArrayAsync(
                    $"https://raw.githubusercontent.com/microsoft/TypeScript/{manifest.SourceRevision}/tests/cases/{path}",
                    ct)
                : await File.ReadAllBytesAsync(Path.Combine(checkout, "tests/cases", path), ct);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            cases.AddRange(SplitUnits(path, Encoding.UTF8.GetString(bytes), hash));
        }

        if (cases.Count == 0 || cases
            .Select(test => test.Id)
            .Distinct(StringComparer.Ordinal)
            .Count() != cases.Count)
        {
            throw new InvalidDataException("Corpus must contain source units with unique IDs.");
        }

        var input = JsonSerializer.Serialize(new CompilerInput(manifest.CompilerVersion, cases), _jsonContext.CompilerInput);
        var result = await RunCompiler(compilerPath, input, ct);
        using var compilerOutput = JsonDocument.Parse(result);
        var corpus = new Corpus(manifest.CompilerVersion, manifest.SourceRevision, compilerOutput.RootElement.GetProperty("cases"));
        var content = JsonSerializer.Serialize(corpus, _jsonContext.Corpus) + "\n";
        var outputPath = Path.Combine(directory, "corpus.json");
        if (check)
        {
            if (await File.ReadAllTextAsync(outputPath, ct) != content)
            {
                throw new InvalidDataException("Corpus differs. Regenerate and review the diagnostic and syntax changes.");
            }
        }
        else
        {
            var temporaryPath = outputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, content, ct);
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        Console.WriteLine($"{(check ? "Verified" : "Generated")} {cases.Count} TypeScript/TSX units from {manifest.Files.Count} upstream files.");
    }

    private static IEnumerable<SourceCase> SplitUnits(string path, string input, string hash)
    {
        var name = Path.GetFileName(path);
        var lines = new List<string>();
        foreach (var line in input.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = Regex.Match(
                line,
                @"^\s*//\s*@filename\s*:\s*(.+?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                if (IsTypeScript(name) && lines.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    yield return new SourceCase($"{path}::{name}", name, string.Join('\n', lines), hash);
                }

                name = match.Groups[1].Value;
                lines.Clear();
            }
            else
            {
                // Harness directives are options, not source; preserve their line positions.
                lines.Add(Regex.IsMatch(line, @"^\s*//\s*@\w+\s*:") ? string.Empty : line);
            }
        }

        if (IsTypeScript(name) && lines.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return new SourceCase($"{path}::{name}", name, string.Join('\n', lines), hash);
        }
    }

    private static bool IsTypeScript(string path)
        => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> RunCompiler(string compilerPath, string input, CancellationToken ct)
    {
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("--eval");
        start.ArgumentList.Add(CompilerAdapter);
        start.ArgumentList.Add(compilerPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the reference TypeScript compiler.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            var errorText = await error;
            var outputText = await output;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Reference compiler failed: {errorText}");
            }

            return outputText;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    // This adapter only invokes the official compiler API. Corpus selection, downloads,
    // normalization, provenance and updates belong to this .NET tool. No JavaScript files are generated.
    private const string CompilerAdapter = """
        const ts = require(process.argv[1]);
        const input = JSON.parse(require('node:fs').readFileSync(0, 'utf8'));
        if (ts.version !== input.compilerVersion) throw Error(`Expected TypeScript ${input.compilerVersion}, received ${ts.version}`);
        const cases = input.cases.map(test => {
            const kind = test.fileName.toLowerCase().endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
            const source = ts.createSourceFile(test.fileName, test.sourceText,
                { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode: ts.JSDocParsingMode.ParseNone }, true, kind);
            let hasVariance = false;
            function visit(node) {
                if (ts.isTypeParameterDeclaration(node) && node.modifiers?.some(m => m.kind === ts.SyntaxKind.InKeyword || m.kind === ts.SyntaxKind.OutKeyword)) hasVariance = true;
                ts.forEachChild(node, visit);
            }
            visit(source);
            let grammar = [];
            if (hasVariance) {
                // TypeScript exposes variance order/duplicate grammar errors through semantic diagnostics.
                const options = { noLib: true, noResolve: true, types: [] };
                const name = require('node:path').resolve(test.fileName);
                const checked = ts.createSourceFile(name, test.sourceText, ts.ScriptTarget.Latest, true, kind);
                const host = ts.createCompilerHost(options);
                host.getSourceFile = fileName => require('node:path').resolve(fileName) === name ? checked : undefined;
                grammar = ts.createProgram([name], options, host).getSemanticDiagnostics(checked)
                    .filter(d => [1029, 1030].includes(d.code) && ['in', 'out'].includes(test.sourceText.slice(d.start, d.start + d.length)));
            }
            return { ...test,
                diagnostics: [...source.parseDiagnostics, ...grammar].sort((a, b) => a.start - b.start)
                    .map(d => ({ code: d.code, start: d.start, length: d.length })),
                statements: source.statements.map(node => ({
                    kind: node.kind === ts.SyntaxKind.VariableStatement ? 'VariableStatement' : ts.SyntaxKind[node.kind],
                    start: node.getStart(source), end: node.end }))
            };
        });
        process.stdout.write(JSON.stringify({ cases }));
        """;

    private sealed record Manifest(string CompilerVersion, string SourceRevision, IReadOnlyList<string> Files);

    private sealed record SourceCase(string Id, string FileName, string SourceText, string UpstreamFileSha256);

    private sealed record CompilerInput(string CompilerVersion, IReadOnlyList<SourceCase> Cases);

    private sealed record Corpus(string CompilerVersion, string SourceRevision, JsonElement Cases);

    [JsonSerializable(typeof(Manifest))]
    [JsonSerializable(typeof(CompilerInput))]
    [JsonSerializable(typeof(Corpus))]
    private sealed partial class CorpusJsonContext : JsonSerializerContext;
}
