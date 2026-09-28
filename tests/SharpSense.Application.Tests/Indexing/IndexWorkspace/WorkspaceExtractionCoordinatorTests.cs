using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Collections.Concurrent;

namespace SharpSense.Application.Tests.Indexing.IndexWorkspace;

public sealed class WorkspaceExtractionCoordinatorTests
{
    [Fact]
    public async Task WhenDocumentationUpdates_ThenReuseCodeAcrossTransientHandlersAndKeepCompleteMergedGraph()
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with
        {
            Edges = [new("csharp", "typescript", EdgeType.MethodCall)],
            Diagnostics = ["C# warning retained"]
        };
        (await fixture.Run()).IsSuccess.Should().BeTrue();

        fixture.Markdown.Value = Graph("updated-guide", "docs/guide.md") with
        {
            Edges = [new("updated-guide", "typescript", EdgeType.DocumentLink)]
        };
        (await fixture.Run(Change("docs/guide.md"))).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(1);
        fixture.TypeScript.Calls.Should().Be(1);
        fixture.Markdown.Calls.Should().Be(2);
        fixture.Persisted!.CodeNodes.Select(node => node.CanonicalId)
            .Order().Should().Equal(["csharp", "typescript", "updated-guide"]);
        fixture.Persisted.Diagnostics.Should().Contain("C# warning retained");
        fixture.Persisted.Edges.Count.Should().Be(2);

        fixture.Markdown.Value = new([], [], [], []);
        (await fixture.Run(Change("docs/guide.md", WorkspaceFileChangeAction.Deleted))).IsSuccess.Should().BeTrue();
        fixture.Persisted!.CodeNodes.Select(node => node.CanonicalId)
            .Order().Should().Equal(["csharp", "typescript"]);
        fixture.Persisted.Edges.Should().ContainSingle();
        fixture.CSharp.Calls.Should().Be(1);

        fixture.CSharp.Value = Graph("changed-class", "Backend/Api.cs");
        (await fixture.Run(Change("Backend/Api.cs"))).IsSuccess.Should().BeTrue();
        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(2);
        fixture.Persisted!.CodeNodes.Should().Contain(node => node.CanonicalId == "changed-class");
        fixture.Persisted.CodeNodes.Should().NotContain(node => node.CanonicalId == "csharp");
        fixture.Commits.Should().Be(4);
    }

    [Theory]
    [InlineData(WorkspaceFileChangeAction.Added)]
    [InlineData(WorkspaceFileChangeAction.Renamed)]
    public async Task WhenNewMarkdownMembership_ThenRefreshesCSharpButReusesTypeScript(WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        (await fixture.Run()).IsSuccess.Should().BeTrue();

        (await fixture.Run(new WorkspaceFileChange(
            action,
            OldPath: action == WorkspaceFileChangeAction.Renamed ? "docs/old.md" : null,
            NewPath: "docs/new.md"))).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(1);
        fixture.Markdown.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("Backend/Api.cs", WorkspaceFileChangeAction.Modified)]
    [InlineData("frontend/tsconfig.json", WorkspaceFileChangeAction.Modified)]
    [InlineData("Directory.Build.props", WorkspaceFileChangeAction.Modified)]
    [InlineData("docs", WorkspaceFileChangeAction.DirectoryDeleted)]
    [InlineData("docs", WorkspaceFileChangeAction.DirectoryRenamed)]
    public async Task WhenCodeOrDirectoriesChange_ThenEveryContributionIsRefreshed(string path, WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        (await fixture.Run()).IsSuccess.Should().BeTrue();

        (await fixture.Run(Change(path, action))).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(2);
        fixture.Markdown.Calls.Should().Be(2);
    }

    [Fact]
    public async Task WhenDeclaredMarkdownInputsAndNonOptedInExtractors_ThenAreNotReused()
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with
        {
            InputPaths = ["/repo/docs/schema.md"]
        };
        fixture.TypeScript.Value = fixture.TypeScript.Value with
        {
            CanReuseForDocumentationChanges = false
        };
        (await fixture.Run()).IsSuccess.Should().BeTrue();

        (await fixture.Run(Change("docs/schema.md"))).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(2);
    }

    [Fact]
    public async Task WhenFullRunsChangedSelectionsAndColdSessions_ThenNeverReusePriorContributions()
    {
        using var fixture = new Fixture();
        (await fixture.Run(Change("docs/guide.md"))).IsSuccess.Should().BeTrue();
        (await fixture.Run()).IsSuccess.Should().BeTrue();
        fixture.Options.WorkspaceSources = [new(WorkspaceSourceKind.TypeScript, "other-frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")];
        (await fixture.Run(Change("docs/guide.md"))).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(3);
        fixture.Persisted!.CodeNodes.Should().NotContain(node => node.CanonicalId == "csharp");
        fixture.TypeScript.Contexts.Last().TargetPath.Should().Be("/repo/other-frontend");
    }

    [Theory]
    [InlineData(WorkspaceFileChangeAction.Modified)]
    [InlineData(WorkspaceFileChangeAction.Deleted)]
    public async Task WhenDeclaredMarkdownInputCaseVaries_ThenCSharpContributionIsInvalidated(WorkspaceFileChangeAction action)
    {
        using var fixture = new Fixture();
        fixture.CSharp.Value = fixture.CSharp.Value with
        {
            InputPaths = ["/repo/docs/schema.md"]
        };
        (await fixture.Run()).IsSuccess.Should().BeTrue();
        fixture.CSharp.Value = Graph("updated-csharp", "Backend/Api.cs");

        var change = action == WorkspaceFileChangeAction.Deleted
            ? new WorkspaceFileChange(action, OldPath: "/repo/Docs/SCHEMA.md")
            : new WorkspaceFileChange(action, NewPath: "/repo/Docs/SCHEMA.md");
        (await fixture.Run(change)).IsSuccess.Should().BeTrue();

        fixture.CSharp.Calls.Should().Be(2);
        fixture.TypeScript.Calls.Should().Be(1);
        fixture.Persisted!.CodeNodes.Should().Contain(node => node.CanonicalId == "updated-csharp");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenFailedExtractionOr_ThenCommitPreservesPreviousGraphAndInvalidatesReuse(bool persistenceFailure)
    {
        using var fixture = new Fixture();
        (await fixture.Run()).IsSuccess.Should().BeTrue();
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

        (await fixture.Run(Change("Backend/Api.cs"))).IsFailed.Should().BeTrue();
        fixture.Persisted.Should().BeSameAs(previous);
        var callsBeforeRecovery = fixture.CSharp.Calls;
        fixture.FailCommit = false;
        fixture.CSharp.Error = null;

        (await fixture.Run(Change("docs/guide.md"))).IsSuccess.Should().BeTrue();
        fixture.CSharp.Calls.Should().Be(callsBeforeRecovery + 1);
        fixture.Persisted!.CodeNodes.Should().Contain(node => node.CanonicalId == "changed-class");
        fixture.Commits.Should().Be(2);
    }

    [Fact]
    public async Task WhenLanguageWorkers_ThenOverlapSynchronousWorkButKeepEachLanguageAndProgressSerial()
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
                    entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("Synchronous language work did not overlap.");
                }
                context.Progress?.Report(new("worker", 0, 1));
                Interlocked.Decrement(ref active);

                return Task.FromResult(Result.Ok(worker.Value));
            };
        }

        var progress = new RecordingProgress();
        var result = await fixture.Handler()
            .Handle(new IndexWorkspaceCommand(progress), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        maximum.Should().Be(3);
        fixture.Workers.Should().AllSatisfy(worker => worker.MaximumActive.Should().Be(1));
        progress.MaximumActive.Should().Be(1);
        fixture.CSharp.Calls.Should().Be(2);
        fixture.Commits.Should().Be(1);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task WhenWorkerFailureOrCancellation_ThenJoinsSiblingsBeforeReturning(string failure)
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

        var result = await fixture.Handler()
            .Handle(new IndexWorkspaceCommand(), cancellation.Token);

        result.IsFailed.Should().BeTrue();
        stopped.Task.IsCompletedSuccessfully.Should().BeTrue();
        fixture.Commits.Should().Be(0);
        fixture.Workers.Should().AllSatisfy(worker => worker.Active.Should().Be(0));
        result.Errors.Should().Contain(error => error.Message.Contains(failure == "cancel" ? "cancelled" : "Documentation failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenCancelledCodeRefresh_ThenPreservesGraphAndRequiresFreshContributionsBeforeNextDocsCommit()
    {
        using var fixture = new Fixture();
        (await fixture.Run()).IsSuccess.Should().BeTrue();
        var previous = fixture.Persisted;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.CSharp.OnExtract = async (_, token) =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(token);
        };

        var result = await fixture.Handler()
            .Handle(new IndexWorkspaceCommand(ChangedFiles: [Change("Backend/Api.cs")]), cancellation.Token);
        result.IsFailed.Should().BeTrue();
        fixture.Persisted.Should().BeSameAs(previous);
        var callsBeforeRecovery = fixture.CSharp.Calls;
        fixture.CSharp.OnExtract = null;
        fixture.CSharp.Value = Graph("changed-class", "Backend/Api.cs");

        (await fixture.Run(Change("docs/guide.md"))).IsSuccess.Should().BeTrue();
        fixture.CSharp.Calls.Should().Be(callsBeforeRecovery + 1);
        fixture.Persisted!.CodeNodes.Should().Contain(node => node.CanonicalId == "changed-class");
    }

    [Fact]
    public void WhenCoordinatorRegistration_ThenSharesOnlyOneServiceScope()
    {
        var services = new ServiceCollection().AddIndexing();
        var descriptor = services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(WorkspaceExtractionCoordinator)).Which;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    private static WorkspaceFileChange Change(string path, WorkspaceFileChangeAction action = WorkspaceFileChangeAction.Modified)
        => new(action, NewPath: path);

    private static ExtractedNodes Graph(string id, string path)
        => new(
            [],
            [new(
                id,
                null,
                id,
                id,
                path.EndsWith(".md", StringComparison.Ordinal) ? NodeType.Document : NodeType.Class,
                path,
                1,
                1,
                "",
                id)],
            [],
            [],
            CanReuseForDocumentationChanges: true);

    private sealed class Fixture : IDisposable
    {
        public Worker CSharp
        {
            get;
        } = new(WorkspaceSourceKind.CSharp, Graph("csharp", "Backend/Api.cs"));
        public Worker TypeScript
        {
            get;
        } = new(WorkspaceSourceKind.TypeScript, Graph("typescript", "frontend/index.ts"));
        public Worker Markdown
        {
            get;
        } = new(WorkspaceSourceKind.Markdown, Graph("guide", "docs/guide.md"));
        public Worker[] Workers => [CSharp, TypeScript, Markdown];
        public WorkspaceExecutionOptions Options
        {
            get;
        } = new()
        {
            WorkspaceId = "fixture",
            WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "Backend.sln"), new(WorkspaceSourceKind.TypeScript, "frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
            SkipEmbeddings = true,
            DisableEmbeddingCache = true
        };
        public ExtractedNodes? Persisted
        {
            get; private set;
        }
        public int Commits
        {
            get; private set;
        }
        public bool FailCommit
        {
            get; set;
        }
        private readonly WorkspacePaths _paths = new();
        private readonly Mock<IKnowledgeGraphRepository> _repository = new(MockBehavior.Strict);
        private readonly WorkspaceExtractionCoordinator _coordinator;

        public Fixture()
        {
            _coordinator = new(
                Workers,
                _paths,
                Moq.Mock.Of<IWorkspaceChangeFilter>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance);
            _repository.Setup(repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
                .Callback<ExtractedNodes, CancellationToken>((nodes, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (FailCommit)
                    {
                        throw new IOException("Fixture transaction rolled back");
                    }
                    Workers.Should().AllSatisfy(worker => worker.Active.Should().Be(0));
                    Persisted = nodes;
                    Commits++;
                })
                .Returns(Task.CompletedTask);
        }

        // Initial index and watch updates resolve separate transient handlers in production.
        public IndexWorkspaceCommandHandler Handler()
            => new IndexWorkspaceCommandHandler(
                new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object,
                _repository.Object,
                _paths,
                Microsoft.Extensions.Options.Options.Create(Options),
                _coordinator,
                Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);

        public Task<Result<IndexWorkspaceOutcome>> Run(params WorkspaceFileChange[] changes)
            => Handler().Handle(
                new IndexWorkspaceCommand(ChangedFiles: changes.Length == 0 ? null : changes),
                TestContext.Current.CancellationToken);

        public void Dispose() => _coordinator.Dispose();
    }

    private sealed class Worker(WorkspaceSourceKind kind, ExtractedNodes value) : ILanguageExtractor
    {
        public WorkspaceSourceKind SourceKind => kind;
        public ExtractedNodes Value
        {
            get; set;
        } = value;
        public string? Error
        {
            get; set;
        }
        public Func<ExtractionContext, CancellationToken, Task<Result<ExtractedNodes>>>? OnExtract
        {
            get; set;
        }
        public ConcurrentQueue<ExtractionContext> Contexts
        {
            get;
        } = new();
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
