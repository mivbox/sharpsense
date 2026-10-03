using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class InMemoryContextFactoryTests
{
    [Fact]
    public async Task WhenAlreadyCancelled_ThenDoesNotCreateAContext()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var created = false;
        await using var factory = new InMemoryContextFactory<DbContext>(options =>
        {
            created = true;

            return new DbContext(options);
        });

        var create = () => factory.GetContext(cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        created.Should().BeFalse();
    }

    [Fact]
    public async Task WhenInitializationIsCancelled_ThenDisposesTheContextAndAllowsRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        DbContext? created = null;
        await using var factory = new InMemoryContextFactory<DbContext>(options =>
        {
            var context = new DbContext(options);
            if (created is null)
            {
                created = context;
                cancellation.Cancel();
            }

            return context;
        });

        var create = () => factory.GetContext(cancellation.Token);

        await create.Should().ThrowAsync<OperationCanceledException>();
        created.Should().NotBeNull();
        var useDisposedContext = () => created!.Database.CanConnectAsync(ct);
        await useDisposedContext.Should().ThrowExactlyAsync<ObjectDisposedException>();

        await using var retry = await factory.GetContext(ct);
        (await retry.Database.CanConnectAsync(ct)).Should().BeTrue();
    }
}
