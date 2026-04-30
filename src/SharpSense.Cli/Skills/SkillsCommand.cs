using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharpSense.Cli.Shared;
using Spectre.Console.Cli;
using System.IO.Abstractions;

namespace SharpSense.Cli.Skills;

[UsedImplicitly]
internal sealed class SkillsCommand : AbstractAsyncCommand<SkillsCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<SkillsCommand>();

    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[install-root]")]
        public string? InstallRoot { get; init; }
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<SkillPackInstaller>();
    }

    protected override Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        var fileSystem = host.Services.GetRequiredService<IFileSystem>();
        var installRoot = ResolveInstallRoot(fileSystem, settings.InstallRoot);
        var installer = host.Services.GetRequiredService<SkillPackInstaller>();
        Logger.Information("Installing embedded skills into install root {InstallRoot}", installRoot);
        var result = installer.Install(installRoot);
        Logger.Information(
            "Installed {InstalledFileCount} embedded skill files into {SkillsDirectoryPath}",
            result.InstalledFiles.Length,
            result.SkillsDirectoryPath);
        var output = string.Join(
            Environment.NewLine,
            new[] { $"Installed {result.InstalledFiles.Length} skill files to {result.SkillsDirectoryPath}." }
                .Concat(result.InstalledFiles.Select(static file => $"- {file}")));

        CommandOutput.Write(context, output);
        Logger.Information("Finished writing skills command output.");
        return Task.FromResult(0);
    }

    private static string ResolveInstallRoot(
        IFileSystem fileSystem,
        string? installRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var workingDirectory = fileSystem.Directory.GetCurrentDirectory();
        var rootCandidate = string.IsNullOrWhiteSpace(installRoot)
            ? workingDirectory
            : fileSystem.Path.IsPathRooted(installRoot)
                ? installRoot
                : fileSystem.Path.Combine(workingDirectory, installRoot);

        return fileSystem.Path.GetFullPath(rootCandidate);
    }
}
