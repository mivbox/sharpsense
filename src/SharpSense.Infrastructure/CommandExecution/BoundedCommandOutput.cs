using System.Text;

namespace SharpSense.Infrastructure.CommandExecution;

/// <summary>Shares capture limits across streams while draining every logical line.</summary>
internal sealed class BoundedCommandOutput(
    int maxCapturedLines,
    int maxCapturedBytes,
    Func<string, CancellationToken, Task> onOutput) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _capturedLines;
    private int _capturedBytes;
    private bool _captureStopped;

    public int TotalLines { get; private set; }

    public bool Truncated { get; private set; }

    public async Task Read(TextReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        var line = new StringBuilder(Math.Min(1024, maxCapturedBytes));
        var hasContent = false;
        var lineTruncated = false;
        var previousWasCarriageReturn = false;
        int read;

        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (previousWasCarriageReturn && character == '\n')
                {
                    previousWasCarriageReturn = false;
                    continue;
                }

                previousWasCarriageReturn = character == '\r';
                if (character is '\r' or '\n')
                {
                    await CaptureLine(line, lineTruncated, ct);
                    line.Clear();
                    hasContent = false;
                    lineTruncated = false;
                    continue;
                }

                hasContent = true;
                // A UTF-8 prefix cannot contain more UTF-16 code units than bytes.
                // Bound allocation before the newline, then apply the shared byte budget.
                if (!Volatile.Read(ref _captureStopped) && line.Length < maxCapturedBytes)
                {
                    line.Append(character);
                }
                else
                {
                    lineTruncated = true;
                }
            }
        }

        if (hasContent)
        {
            await CaptureLine(line, lineTruncated, ct);
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task CaptureLine(StringBuilder line, bool lineTruncated, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (TotalLines < int.MaxValue)
            {
                TotalLines++;
            }

            if (_captureStopped || _capturedLines >= maxCapturedLines)
            {
                StopCapture();
                return;
            }

            // The allocation ceiling may have cut between a surrogate pair.
            if (lineTruncated && line.Length > 0 && char.IsHighSurrogate(line[^1]))
            {
                line.Length--;
            }

            var text = line.ToString();
            var retainedCharacters = 0;
            var retainedBytes = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (retainedBytes + rune.Utf8SequenceLength > maxCapturedBytes - _capturedBytes)
                {
                    break;
                }

                retainedBytes += rune.Utf8SequenceLength;
                retainedCharacters += rune.Utf16SequenceLength;
            }

            if (lineTruncated || retainedCharacters < text.Length)
            {
                StopCapture();
            }

            if (retainedCharacters > 0 || text.Length == 0 && !lineTruncated)
            {
                await onOutput(text[..retainedCharacters], ct);
                _capturedLines++;
                _capturedBytes += retainedBytes;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopCapture()
    {
        Truncated = true;
        Volatile.Write(ref _captureStopped, true);
    }
}
