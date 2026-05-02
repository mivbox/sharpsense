using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Refactoring;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Refactoring;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.IO.Abstractions;

namespace SharpSense.Cli.Refactor;

[UsedImplicitly]
internal sealed class RefactorCommand : AbstractAsyncCommand<RefactorCommand.Settings>
{
    private static readonly string[] SupportedReplacementFileExtensions = [".cs", ".txt"];

    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--node-id <NODE_ID>")]
        public int NodeId { get; init; }

        [CommandOption("--file <path>")]
        public string? FilePath { get; init; }

        [CommandOption("--target <path>")]
        public string? TargetPath { get; init; }

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        public override ValidationResult Validate()
        {
            if (NodeId <= 0)
            {
                return ValidationResult.Error("A positive node id is required.");
            }

            return !string.IsNullOrWhiteSpace(FilePath) && !HasSupportedReplacementFileExtension(FilePath)
                ? ValidationResult.Error("The replacement file must use a .cs or .txt extension.")
                : ValidationResult.Success();
        }
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        var rawRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.RepositoryRoot = rawRoot;
        });
        services.AddRepositoryWorkspace(rawRoot);
        services.AddRefactoring();
        services.AddRefactoringInfrastructure();
        services.AddIndexingInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var fileSystem = services.GetRequiredService<IFileSystem>();
        var repositoryRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var replacementCode = await ReadReplacementCode(
            settings,
            repositoryRoot,
            fileSystem,
            ct);
        var result = replacementCode.Success
            ? await services
                .GetRequiredService<INodeRefactorer>()
                .RefactorNode(
                    settings.NodeId,
                    replacementCode.SourceCode,
                    settings.TargetPath,
                    ct)
            : new RefactorResult(
                false,
                [],
                replacementCode.ErrorMessage);

        CommandOutput.Write(context, TokenObjectNotation.SerializeRefactorResult(result));
        return result.Success ? 0 : 1;
    }

    private static async Task<ReplacementCodeResult> ReadReplacementCode(
        Settings settings,
        string repositoryRoot,
        IFileSystem fileSystem,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(settings.FilePath))
        {
            return new ReplacementCodeResult(
                true,
                await Console.In.ReadToEndAsync(ct),
                string.Empty);
        }

        var absoluteFilePath = fileSystem.Path.GetFullPath(
            fileSystem.Path.IsPathRooted(settings.FilePath)
                ? settings.FilePath
                : fileSystem.Path.Combine(
                    repositoryRoot,
                    settings.FilePath));
        if (!fileSystem.File.Exists(absoluteFilePath))
        {
            return Failure($"Replacement file '{absoluteFilePath}' was not found.");
        }

        if (!HasSupportedReplacementFileExtension(absoluteFilePath))
        {
            return Failure($"Replacement file '{absoluteFilePath}' must use a .cs or .txt extension.");
        }

        return new ReplacementCodeResult(
            true,
            await fileSystem.File.ReadAllTextAsync(absoluteFilePath, ct),
            string.Empty);
    }

    private static bool HasSupportedReplacementFileExtension(string path)
    {
        var extension = Path.GetExtension(path);

        return SupportedReplacementFileExtensions.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase);
    }

    private static ReplacementCodeResult Failure(string errorMessage)
        => new(
            false,
            string.Empty,
            errorMessage);

    private readonly record struct ReplacementCodeResult(
        bool Success,
        string SourceCode,
        string ErrorMessage);
}
