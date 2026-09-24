using System.Collections.Concurrent;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing.IndexTarget;

public sealed class WorkspaceExtractionCoordinatorTests
{
    [Fact]
    public async Task DocumentationUpdatesReuseCodeAcrossTransientHandlersAndKeepCompleteMergedGraph()
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with
        {
            Edges = [new("csharp", "typescript", EdgeType.MethodCall)],
            Diagnostics = ["C# warning retained"]
        };
        Assert.True((await fixture.Run()).IsSuccess);

        fixture.Markdown.Value = Graph("updated-guide", "docs/guide.md") with
        {
            Edges = [new("updated-guide", "typescript", EdgeType.DocumentLink)]
        };
        Assert.True((await fixture.Run(Change("docs/guide.md"))).IsSuccess);

        Assert.Equal(1, fixture.CSharp.Calls);
        Assert.Equal(1, fixture.TypeScript.Calls);
        Assert.Equal(2, fixture.Markdown.Calls);
        Assert.Equal(["csharp", "typescript", "updated-guide"], fixture.Persisted!.CodeNodes.Select(node => node.CanonicalId).Order());
        Assert.Contains("C# warning retained", fixture.Persisted.Diagnostics);
        Assert.Equal(2, fixture.Persisted.Edges.Count);

        fixture.Markdown.Value = new([], [], [], []);
        Assert.True((await fixture.Run(Change("docs/guide.md", WorkspaceFileChangeAction.Deleted))).IsSuccess);
        Assert.Equal(["csharp", "typescript"], fixture.Persisted!.CodeNodes.Select(node => node.CanonicalId).Order());
        Assert.Single(fixture.Persisted.Edges);
        Assert.Equal(1, fixture.CSharp.Calls);

        fixture.CSharp.Value = Graph("changed-class", "Backend/Api.cs");
        Assert.True((await fixture.Run(Change("Backend/Api.cs"))).IsSuccess);
        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(2, fixture.TypeScript.Calls);
        Assert.Contains(fixture.Persisted!.CodeNodes, node => node.CanonicalId == "changed-class");
        Assert.DoesNotContain(fixture.Persisted.CodeNodes, node => node.CanonicalId == "csharp");
        Assert.Equal(4, fixture.Commits);
    }

    [Theory]
    [InlineData(WorkspaceFileChangeAction.Added)]
    [InlineData(WorkspaceFileChangeAction.Renamed)]
    public async Task NewMarkdownMembershipRefreshesCSharpButReusesTypeScript(WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Run()).IsSuccess);

        Assert.True((await fixture.Run(new WorkspaceFileChange(action, OldPath: action == WorkspaceFileChangeAction.Renamed ? "docs/old.md" : null,
            NewPath: "docs/new.md"))).IsSuccess);

        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(1, fixture.TypeScript.Calls);
        Assert.Equal(2, fixture.Markdown.Calls);
    }

    [Theory]
    [InlineData("Backend/Api.cs", WorkspaceFileChangeAction.Modified)]
    [InlineData("frontend/tsconfig.json", WorkspaceFileChangeAction.Modified)]
    [InlineData("Directory.Build.props", WorkspaceFileChangeAction.Modified)]
    [InlineData("docs", WorkspaceFileChangeAction.DirectoryDeleted)]
    [InlineData("docs", WorkspaceFileChangeAction.DirectoryRenamed)]
    public async Task NonDocumentationAndDirectoryChangesRefreshEveryContribution(string path, WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Run()).IsSuccess);

        Assert.True((await fixture.Run(Change(path, action))).IsSuccess);

        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(2, fixture.TypeScript.Calls);
        Assert.Equal(2, fixture.Markdown.Calls);
    }

    [Fact]
    public async Task DeclaredMarkdownInputsAndNonOptedInExtractorsAreNotReused()
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with { InputPaths = ["/repo/docs/schema.md"] };
        fixture.TypeScript.Value = fixture.TypeScript.Value with { CanReuseForDocumentationChanges = false };
        Assert.True((await fixture.Run()).IsSuccess);

        Assert.True((await fixture.Run(Change("docs/schema.md"))).IsSuccess);

        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(2, fixture.TypeScript.Calls);
    }

    [Fact]
    public async Task FullRunsChangedSelectionsAndColdSessionsNeverReusePriorContributions()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Run(Change("docs/guide.md"))).IsSuccess);
        Assert.True((await fixture.Run()).IsSuccess);
        fixture.Options.WorkspaceSources = [new(WorkspaceSourceKind.TypeScript, "other-frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")];
        Assert.True((await fixture.Run(Change("docs/guide.md"))).IsSuccess);

        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(3, fixture.TypeScript.Calls);
        Assert.DoesNotContain(fixture.Persisted!.CodeNodes, node => node.CanonicalId == "csharp");
        Assert.Equal("/repo/other-frontend", fixture.TypeScript.Contexts.Last().TargetPath);
    }

    [Theory]
    [InlineData(WorkspaceFileChangeAction.Modified)]
    [InlineData(WorkspaceFileChangeAction.Deleted)]
    public async Task DeclaredMarkdownInputCaseVariantsInvalidateCSharpContribution(WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with { InputPaths = ["/repo/docs/schema.md"] };
        Assert.True((await fixture.Run()).IsSuccess);
        fixture.CSharp.Value = Graph("updated-csharp", "Backend/Api.cs");

        var change = action == WorkspaceFileChangeAction.Deleted
            ? new WorkspaceFileChange(action, OldPath: "/repo/Docs/SCHEMA.md")
            : new WorkspaceFileChange(action, NewPath: "/repo/Docs/SCHEMA.md");
        Assert.True((await fixture.Run(change)).IsSuccess);

        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(1, fixture.TypeScript.Calls);
        Assert.Contains(fixture.Persisted!.CodeNodes, node => node.CanonicalId == "updated-csharp");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedExtractionOrCommitPreservesPreviousGraphAndInvalidatesReuse(bool persistenceFailure)
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Run()).IsSuccess);
        var previous = fixture.Persisted;
        fixture.CSharp.Value = Graph("changed-class", "Backend/Api.cs");
        if (persistenceFailure)
        {
            fixture.FailCommit = true;
        }
        else
        {
            fixture.CSharp.Error = "C# extraction failed";
        }

        Assert.True((await fixture.Run(Change("Backend/Api.cs"))).IsFailed);
        Assert.Same(previous, fixture.Persisted);
        var callsBeforeRecovery = fixture.CSharp.Calls;
        fixture.FailCommit = false;
        fixture.CSharp.Error = null;

        Assert.True((await fixture.Run(Change("docs/guide.md"))).IsSuccess);
        Assert.Equal(callsBeforeRecovery + 1, fixture.CSharp.Calls);
        Assert.Contains(fixture.Persisted!.CodeNodes, node => node.CanonicalId == "changed-class");
        Assert.Equal(2, fixture.Commits);
    }

    [Fact]
    public async Task LanguageWorkersOverlapSynchronousWorkButKeepEachLanguageAndProgressSerial()
    {
        using var fixture = new Fixture();
        fixture.Options.WorkspaceSources =
        [
            new(WorkspaceSourceKind.CSharp, "one.sln"),
            new(WorkspaceSourceKind.CSharp, "two.sln"),
            new(WorkspaceSourceKind.TypeScript, "frontend"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md")
        ];
        using var entered = new CountdownEvent(3);
        var active = 0;
        var maximum = 0;
        foreach (var worker in fixture.Workers)
        {
            worker.OnExtract = (context, _) =>
            {
                var count = Interlocked.Increment(ref active);
                lock (entered)
                {
                    maximum = Math.Max(count, maximum);
                }
                if (worker.Calls == 1)
                {
                    entered.Signal();
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "Synchronous language work did not overlap.");
                }
                context.Progress?.Report(new("worker", 0, 1));
                Interlocked.Decrement(ref active);
                return Task.FromResult(Result.Ok(worker.Value));
            };
        }

        var progress = new RecordingProgress();
        var result = await fixture.Handler().Handle(new IndexTargetCommand(progress), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.Equal(3, maximum);
        Assert.All(fixture.Workers, worker => Assert.Equal(1, worker.MaximumActive));
        Assert.Equal(1, progress.MaximumActive);
        Assert.Equal(2, fixture.CSharp.Calls);
        Assert.Equal(1, fixture.Commits);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task WorkerFailureOrCancellationJoinsSiblingsBeforeReturning(string failure)
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.CSharp.OnExtract = async (_, token) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Unreachable");
            }
            finally
            {
                await Task.Yield();
                stopped.SetResult();
            }
        };
        fixture.Markdown.OnExtract = async (_, _) =>
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (failure == "cancel")
            {
                await cancellation.CancelAsync();
            }
            if (failure == "throw")
            {
                throw new IOException("Documentation failed");
            }
            return Result.Fail<ExtractedNodes>("Documentation failed");
        };

        var result = await fixture.Handler().Handle(new IndexTargetCommand(), cancellation.Token);

        Assert.True(result.IsFailed);
        Assert.True(stopped.Task.IsCompletedSuccessfully);
        Assert.Equal(0, fixture.Commits);
        Assert.All(fixture.Workers, worker => Assert.Equal(0, worker.Active));
        Assert.Contains(result.Errors, error => error.Message.Contains(failure == "cancel" ? "cancelled" : "Documentation failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledCodeRefreshPreservesGraphAndRequiresFreshContributionsBeforeNextDocsCommit()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Run()).IsSuccess);
        var previous = fixture.Persisted;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.CSharp.OnExtract = async (_, token) =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(token);
        };

        var result = await fixture.Handler().Handle(new IndexTargetCommand(ChangedFiles: [Change("Backend/Api.cs")]), cancellation.Token);
        Assert.True(result.IsFailed);
        Assert.Same(previous, fixture.Persisted);
        var callsBeforeRecovery = fixture.CSharp.Calls;
        fixture.CSharp.OnExtract = null;
        fixture.CSharp.Value = Graph("changed-class", "Backend/Api.cs");

        Assert.True((await fixture.Run(Change("docs/guide.md"))).IsSuccess);
        Assert.Equal(callsBeforeRecovery + 1, fixture.CSharp.Calls);
        Assert.Contains(fixture.Persisted!.CodeNodes, node => node.CanonicalId == "changed-class");
    }

    [Fact]
    public void CoordinatorRegistrationSharesOnlyOneServiceScope()
    {
        var services = new ServiceCollection().AddIndexing();
        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkspaceExtractionCoordinator));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private static WorkspaceFileChange Change(string path, WorkspaceFileChangeAction action = WorkspaceFileChangeAction.Modified)
        => new(action, NewPath: path);

    private static ExtractedNodes Graph(string id, string path)
        => new([], [new(id, null, id, id, path.EndsWith(".md", StringComparison.Ordinal) ? NodeType.Document : NodeType.Class,
            path, 1, 1, "", id)], [], [], CanReuseForDocumentationChanges: true);

    private sealed class Fixture : IDisposable
    {
        public Worker CSharp { get; } = new(WorkspaceSourceKind.CSharp, Graph("csharp", "Backend/Api.cs"));
        public Worker TypeScript { get; } = new(WorkspaceSourceKind.TypeScript, Graph("typescript", "frontend/index.ts"));
        public Worker Markdown { get; } = new(WorkspaceSourceKind.Markdown, Graph("guide", "docs/guide.md"));
        public Worker[] Workers => [CSharp, TypeScript, Markdown];
        public SharpSenseCliOptions Options { get; } = new()
        {
            WorkspaceId = "fixture",
            WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "Backend.sln"), new(WorkspaceSourceKind.TypeScript, "frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
            SkipEmbeddings = true,
            DisableEmbeddingCache = true
        };
        public ExtractedNodes? Persisted { get; private set; }
        public int Commits { get; private set; }
        public bool FailCommit { get; set; }
        private readonly WorkspacePaths _paths = new();
        private readonly Mock<IKnowledgeGraphRepository> _repository = new(MockBehavior.Strict);
        private readonly WorkspaceExtractionCoordinator _coordinator;

        public Fixture()
        {
            _coordinator = new(Workers, _paths);
            _repository.Setup(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
                .Callback<ExtractedNodes, CancellationToken>((nodes, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (FailCommit)
                    {
                        throw new IOException("Fixture transaction rolled back");
                    }
                    Assert.All(Workers, worker => Assert.Equal(0, worker.Active));
                    Persisted = nodes;
                    Commits++;
                }).Returns(Task.CompletedTask);
        }

        // Initial index and watch updates resolve separate transient handlers in production.
        public IndexTargetCommandHandler Handler()
            => new(Workers, new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object, _repository.Object,
                _paths, Microsoft.Extensions.Options.Options.Create(Options), workspaceExtraction: _coordinator);

        public Task<Result<IndexTargetOutcome>> Run(params WorkspaceFileChange[] changes)
            => Handler().Handle(new IndexTargetCommand(ChangedFiles: changes.Length == 0 ? null : changes), TestContext.Current.CancellationToken);

        public void Dispose() => _coordinator.Dispose();
    }

    private sealed class Worker(WorkspaceSourceKind kind, ExtractedNodes value) : ILanguageExtractor
    {
        public WorkspaceSourceKind? SourceKind => kind;
        public string ExtractorName => kind.ToString();
        public ExtractedNodes Value { get; set; } = value;
        public string? Error { get; set; }
        public Func<ExtractionContext, CancellationToken, Task<Result<ExtractedNodes>>>? OnExtract { get; set; }
        public ConcurrentQueue<ExtractionContext> Contexts { get; } = new();
        public int Calls;
        public int Active;
        public int MaximumActive;

        public async Task<Result<ExtractedNodes>> Extract(ExtractionContext context, CancellationToken ct)
        {
            Contexts.Enqueue(context);
            Interlocked.Increment(ref Calls);
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref Active));
            try
            {
                return OnExtract is null
                    ? Error is null ? Result.Ok(Value) : Result.Fail(Error)
                    : await OnExtract(context, ct);
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }

        public Task<Result<ExtractedNodes>> ExtractIncremental(IncrementalExtractionContext context, CancellationToken ct)
            => throw new InvalidOperationException("Coordinator must extract complete source contributions.");
    }

    private sealed class RecordingProgress : IProgress<IndexingProgress>
    {
        private int _active;
        public int MaximumActive;

        public void Report(IndexingProgress value)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumActive = Math.Max(MaximumActive, active);
            Thread.SpinWait(100_000);
            Interlocked.Decrement(ref _active);
        }
    }

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? path) => Path.GetRelativePath(RootPath, GetRequiredTargetPath(path!));

        public bool TryToRepositoryRelativePath(string? path, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(path);
            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
