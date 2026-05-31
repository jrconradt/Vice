using System.Collections.Concurrent;
using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class CapturingConsoleWriter : IConsoleWriter
{
    public const int MAX_CAPTURED_CHARS = 16 * 1024 * 1024;

    private readonly IConsoleWriter _inner;
    private readonly ConcurrentQueue<string> _captured = new();
    private int _count;
    private int _capturedChars;

    public CapturingConsoleWriter(IConsoleWriter inner)
    {
        _inner = inner;
    }

    public string CapturedOutput
    {
        get
        {
            if (Volatile.Read(ref _count) == 0)
            {
                return string.Empty;
            }

            return string.Concat(_captured);
        }
    }

    public void Write(string text)
    {
        _inner.Write(text);
        Capture(AnsiStripper.Strip(text));
    }

    public void WriteLine(string text)
    {
        _inner.WriteLine(text);
        Capture(AnsiStripper.Strip(text) + Environment.NewLine);
    }

    public void WriteLine()
    {
        _inner.WriteLine();
        Capture(Environment.NewLine);
    }

    public void WriteError(string text)
    {
        _inner.WriteError(text);
    }

    private void Capture(string chunk)
    {
        var priorChars = Interlocked.Add(ref _capturedChars, chunk.Length) - chunk.Length;
        if (priorChars >= MAX_CAPTURED_CHARS)
        {
            return;
        }

        var remaining = MAX_CAPTURED_CHARS - priorChars;
        if (chunk.Length > remaining)
        {
            chunk = chunk[..remaining];
        }

        _captured.Enqueue(chunk);
        Interlocked.Increment(ref _count);
    }

    internal void Reset()
    {
        while (_captured.TryDequeue(out _))
        {
        }
        Volatile.Write(ref _count, 0);
        Volatile.Write(ref _capturedChars, 0);
    }
}
