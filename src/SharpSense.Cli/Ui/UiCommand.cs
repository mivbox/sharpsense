using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Serilog;
using SharpSense.Application.DependencyGraph;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Ui;

internal sealed class UiCommand : AbstractWebAsyncCommand<UiCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<UiCommand>();
    private const string _namespace = "SharpSense.UI";

    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--url <url>")]
        public string Url { get; set; } = "http://localhost:50069";

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }
    }

    protected override void ConfigureServices(Settings settings, IServiceCollection services)
    {
        var repositoryRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        services.AddRepositoryWorkspace(repositoryRoot);
        services.AddSharpSenseConfiguration(repositoryRoot);

        services.AddDependencyGraph();
        services.AddDependencyGraphInfrastructure();
        services.AddPersistence();
    }

    protected override void ConfigureApp(Settings settings, WebApplication app)
    {
        Logger.Information(
            "Configuring UI application for {Url} with embedded asset namespace {EmbeddedNamespace}",
            settings.Url,
            _namespace);

        app.Urls.Add(settings.Url);

        var fileProvider = new ManifestEmbeddedFileProvider(typeof(UiCommand).Assembly, _namespace);

        app.MapGet(
            "/api/graph",
            static async Task<GraphResult> (
                IQueryHandler<GetDependencyGraphQuery, GraphResult> handler,
                CancellationToken ct) =>
                await handler.Handle(new GetDependencyGraphQuery(), ct))
            .AllowAnonymous();

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = CreateContentTypeProvider()
        });

        app.MapFallback(
            async context =>
            {
                var indexFile = fileProvider.GetFileInfo("index.html");
                if (!indexFile.Exists)
                {
                    Logger.Error(
                        "Embedded UI asset {AssetName} was not found in namespace {EmbeddedNamespace}",
                        "index.html",
                        _namespace);

                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync(
                        "Embedded UI assets were not found. Build the frontend before running the UI command.",
                        context.RequestAborted);
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                await using var stream = indexFile.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
            });
    }

    private static FileExtensionContentTypeProvider CreateContentTypeProvider()
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".map"] = "application/json";
        contentTypeProvider.Mappings[".mjs"] = "text/javascript";
        return contentTypeProvider;
    }
}
