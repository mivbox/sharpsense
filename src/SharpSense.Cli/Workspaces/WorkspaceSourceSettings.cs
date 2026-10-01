using SharpSense.Application.Indexing;
using SharpSense.Cli.Shared;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal class WorkspaceSourceSettings : CliSettings
{
    [CommandOption("--workspace-root|--repo-root <path>")]
    [Description("Resolve source paths against this directory. Defaults to the current directory.")]
    public string? WorkspaceRoot { get; init; }

    [CommandOption("--csharp <path>")]
    [Description("A C# project or solution path. May be repeated.")]
    public string[] CSharp { get; init; } = [];

    [CommandOption("--typescript <path>")]
    [Description("A TypeScript configuration or directory path. May be repeated.")]
    public string[] TypeScript { get; init; } = [];

    [CommandOption("--markdown <path-or-glob>")]
    [Description("A Markdown path or quoted glob. May be repeated.")]
    public string[] Markdown { get; init; } = [];

    [CommandOption("--json")]
    [Description("Write machine-readable JSON without interactive prompts.")]
    public bool Json { get; init; }

    public bool HasSources => CSharp.Length + TypeScript.Length + Markdown.Length > 0;

    public WorkspaceSource[] GetSources(string baseDirectory)
        => CSharp
            .Select(path => new WorkspaceSource(WorkspaceSourceKind.CSharp, Path.GetFullPath(path, baseDirectory)))
            .Concat(TypeScript
                .Select(path => new WorkspaceSource(
                    WorkspaceSourceKind.TypeScript,
                    Path.GetFullPath(
                        path,
                        baseDirectory))))
            .Concat(Markdown
                .Select(path => new WorkspaceSource(
                    WorkspaceSourceKind.Markdown,
                    Path.GetFullPath(
                        path,
                        baseDirectory))))
            .ToArray();
}
