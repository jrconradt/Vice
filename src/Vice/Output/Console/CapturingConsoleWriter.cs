using System.Collections.Concurrent;
using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class CapturingConsoleWriter : IConsoleWriter
{
    private readonly IConsoleWriter _inner;
    private readonly ConcurrentQueue<string> _captured = new();
    private int _count;

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
        _captured.Enqueue(AnsiStripper.Strip(text));
        Interlocked.Increment(ref _count);
    }

    public void WriteLine(string text)
    {
        _inner.WriteLine(text);
        _captured.Enqueue(AnsiStripper.Strip(text) + Environment.NewLine);
        Interlocked.Increment(ref _count);
    }

    public void WriteLine()
    {
        _inner.WriteLine();
        _captured.Enqueue(Environment.NewLine);
        Interlocked.Increment(ref _count);
    }

    public void WriteError(string text)
    {
        _inner.WriteError(text);
    }

    internal void Reset()
    {
        while (_captured.TryDequeue(out _))
        {
        }
        Volatile.Write(ref _count, 0);
    }
}
