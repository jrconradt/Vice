using System.Collections.Concurrent;

namespace Vice.Display;

internal sealed class BufferingConsoleWriter : IConsoleWriter, IAsyncDisposable
{
    private readonly IConsoleWriter _inner;
    private readonly ConcurrentQueue<Action<IConsoleWriter>> _buffer = new();
    private int _flushed;

    public BufferingConsoleWriter(IConsoleWriter inner)
    {
        _inner = inner;
    }

    public void Write(string text)
    {
        _buffer.Enqueue(w => w.Write(text));
    }

    public void WriteLine(string text)
    {
        _buffer.Enqueue(w => w.WriteLine(text));
    }

    public void WriteLine()
    {
        _buffer.Enqueue(w => w.WriteLine());
    }

    public void WriteError(string text)
    {
        _buffer.Enqueue(w => w.WriteError(text));
    }

    public void Flush()
    {
        Interlocked.Exchange(ref _flushed, 1);
        while (_buffer.TryDequeue(out var action))
        {
            action(_inner);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _flushed) == 0 && !_buffer.IsEmpty)
        {
            Flush();
        }

        return ValueTask.CompletedTask;
    }
}
