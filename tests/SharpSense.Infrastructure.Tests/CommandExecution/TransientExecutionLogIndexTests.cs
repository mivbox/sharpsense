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
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory<TransientExecutionLogDbContext>(options => new TransientExecutionLogDbContext(options));
        var dbContextFactory = inMemoryFactory.CreateDbContextFactory();
        var factory = new TransientExecutionLogIndexFactory(dbContextFactory);
        await using var index = await factory.Create(ct);

        var firstAppend = await index.AppendLine("Build started", ct);
        var secondAppend = await index.AppendLine("Error: first failure", ct);
        var thirdAppend = await index.AppendLine("Error: second failure", ct);
        var matchResult = await index.FindMatches("Error", ct);
        var rangeResult = await index.ReadRange(new ExecutionLineRange(2, 3), ct);

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
