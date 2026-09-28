using AwesomeAssertions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Infrastructure.CommandExecution;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.CommandExecution;

public sealed class TransientExecutionLogIndexTests
{
    [Fact]
    public async Task WhenAppendingAndSearchingLines_ThenItReturnsMatchedLineNumbersAndRanges()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<TransientExecutionLogDbContext>(options => new TransientExecutionLogDbContext(options));
        var dbContextFactory = inMemoryFactory.CreateDbContextFactory();
        var factory = new TransientExecutionLogIndexFactory(dbContextFactory);
        await using var index = await factory.Create(TestContext.Current.CancellationToken);

        var firstAppend = await index.AppendLine("Build started", TestContext.Current.CancellationToken);
        var secondAppend = await index.AppendLine("Error: first failure", TestContext.Current.CancellationToken);
        var thirdAppend = await index.AppendLine("Error: second failure", TestContext.Current.CancellationToken);
        var matchResult = await index.FindMatches("Error", TestContext.Current.CancellationToken);
        var rangeResult = await index.ReadRange(new ExecutionLineRange(2, 3), TestContext.Current.CancellationToken);

        firstAppend.Value.Should().Be(1);
        secondAppend.Value.Should().Be(2);
        thirdAppend.Value.Should().Be(3);
        matchResult.IsSuccess.Should().BeTrue();
        matchResult.Value.Should().Equal(2, 3);
        rangeResult.IsSuccess.Should().BeTrue();
        rangeResult.Value.Should().Equal(
            new ExecutionLogLine(2, "Error: first failure"),
            new ExecutionLogLine(3, "Error: second failure"));
    }
}
