using AwesomeAssertions;
using SharpSense.Infrastructure.CommandExecution;
using System.Text;

namespace SharpSense.Infrastructure.Tests.CommandExecution;

public sealed class BoundedCommandOutputTests
{
    [Fact]
    public async Task WhenLineHasNoTerminator_ThenBoundsCaptureAndDrainsRemainingStreams()
    {
        var lines = new List<string>();
        using var capture = Capture(lines, 10, 32);
        using var stdout = new StringReader(new string('x', 100_000));
        using var stderr = new StringReader("later\nlast\n");
        var ct = TestContext.Current.CancellationToken;

        await capture.Read(stdout, ct);
        await capture.Read(stderr, ct);

        lines.Should().Equal(new string('x', 32));
        capture.TotalLines.Should().Be(3);
        capture.Truncated.Should().BeTrue();
        stdout.Peek().Should().Be(-1);
        stderr.Peek().Should().Be(-1);
    }

    [Theory]
    [InlineData("é😀Z", 5, "é", true)]
    [InlineData("é😀", 6, "é😀", false)]
    [InlineData("😀", 3, "", true)]
    [InlineData("😀", 4, "😀", false)]
    [InlineData("abc\n", 3, "abc", false)]
    public async Task WhenByteBudgetMeetsUnicodeBoundary_ThenKeepsCompleteScalars(
        string text,
        int budget,
        string expected,
        bool truncated)
    {
        var lines = new List<string>();
        using var capture = Capture(lines, 1, budget);
        using var reader = new ChunkedReader(text);

        await capture.Read(reader, TestContext.Current.CancellationToken);

        string.Concat(lines).Should().Be(expected);
        Encoding.UTF8.GetByteCount(string.Concat(lines)).Should().BeLessThanOrEqualTo(budget);
        capture.TotalLines.Should().Be(1);
        capture.Truncated.Should().Be(truncated);
        if (expected.Length == 0)
        {
            lines.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenTerminatorsCrossReadBoundaries_ThenPreservesLogicalLines()
    {
        var lines = new List<string>();
        using var capture = Capture(lines, 10, 100);
        using var reader = new ChunkedReader("a\r\n\r\nb\rc\nlast");

        await capture.Read(reader, TestContext.Current.CancellationToken);

        lines.Should().Equal("a", "", "b", "c", "last");
        capture.TotalLines.Should().Be(5);
        capture.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task WhenLineLimitIsExceeded_ThenCountsAndDrainsOmittedLines()
    {
        var lines = new List<string>();
        using var capture = Capture(lines, 2, 100);
        using var reader = new StringReader("one\ntwo\nthree\n");

        await capture.Read(reader, TestContext.Current.CancellationToken);

        lines.Should().Equal("one", "two");
        capture.TotalLines.Should().Be(3);
        capture.Truncated.Should().BeTrue();
        reader.Peek().Should().Be(-1);
    }

    [Fact]
    public async Task WhenStreamsShareCapture_ThenUseOneByteBudget()
    {
        var lines = new List<string>();
        using var capture = Capture(lines, 10, 6);
        using var stdout = new ChunkedReader("alpha\nnext\n");
        using var stderr = new ChunkedReader("error\nlast\n");
        var ct = TestContext.Current.CancellationToken;

        await Task.WhenAll(capture.Read(stdout, ct), capture.Read(stderr, ct));

        lines.Sum(Encoding.UTF8.GetByteCount).Should().Be(6);
        capture.TotalLines.Should().Be(4);
        capture.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCancelledDuringCapture_ThenPropagatesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var reader = new StringReader("first\n" + new string('x', 100_000));
        using var capture = new BoundedCommandOutput(
            10,
            32,
            (_, _) =>
            {
                cancellation.Cancel();

                return Task.CompletedTask;
            });

        var read = () => capture.Read(reader, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BoundedCommandOutput Capture(List<string> lines, int maxLines, int maxBytes)
        => new(
            maxLines,
            maxBytes,
            (line, _) =>
            {
                lines.Add(line);

                return Task.CompletedTask;
            });

    private sealed class ChunkedReader(string text) : StringReader(text)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        }
    }
}
