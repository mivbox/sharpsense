using SharpSense.Cli.Workspaces;
using JetBrains.Annotations;
using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Serilog;
using SharpSense.Application.DependencyGraph;
using SharpSense.Application.Memory;
using SharpSense.Application.Indexing;
using SharpSense.Application.WorkspaceExplorer;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Ui.Api;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.WorkspaceExplorer;
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

    }

    protected override void ConfigureServices(Settings settings, IServiceCollection services)
    {
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var address) ||
            !address.IsLoopback || address.Scheme is not ("http" or "https") ||
            address.AbsolutePath != "/" || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException("The workspace UI must use a loopback HTTP or HTTPS address, such as http://localhost:50069.");
        }

        services.AddWorkspaceUiServices(new WorkspaceUiOptions(
            settings.Workspace,
            CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot),
            address));

        services.AddDependencyGraph();
        services.AddDependencyGraphInfrastructure();
        services.AddWorkspaceExplorer();
        services.AddWorkspaceExplorerInfrastructure();
        services.AddMemory();
        services.AddMemoryInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexing();
        services.AddIndexingInfrastructure();
        services.AddIndexRunRecording();
        services.AddWorkspaceIndexing();
        services.AddSingleton<WorkspaceSourceDiscovery>();
        services.AddUiApi();
        services.AddResponseCompression(options =>
        {
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
    }

    protected override void ConfigureApp(Settings settings, WebApplication app)
    {
        Logger.Information(
            "Configuring UI application for {Url} with embedded asset namespace {EmbeddedNamespace}",
            settings.Url,
            _namespace);

        app.Urls.Clear();
        app.Urls.Add(settings.Url);

        var fileProvider = new ManifestEmbeddedFileProvider(typeof(UiCommand).Assembly, _namespace);

        app.UseResponseCompression();
        app.MapUiApi();

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
                if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/openapi"))
                {
                    await Results.Problem(statusCode: 404, title: "Endpoint not found").ExecuteAsync(context);
                    return;
                }
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
        var contentTypeProvider = new FileExtensionContentTypeProvider { Mappings =
            {
                [".map"] = "application/json", [".mjs"] = "text/javascript"
            }
        };
        return contentTypeProvider;
    }


}
