using System.Collections.Concurrent;
using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class BufferingConsoleWriter : IConsoleWriter, IAsyncDisposable
{
    private enum BufferedOp
    {
        Write,
        WriteLine,
        WriteLineEmpty,
        WriteError,
    }

    private readonly struct BufferedWrite
    {
        public BufferedWrite(BufferedOp op, string? text)
        {
            Op = op;
            Text = text;
        }

        public BufferedOp Op { get; }
        public string? Text { get; }
    }

    private readonly IConsoleWriter _inner;
    private readonly ConcurrentQueue<BufferedWrite> _buffer = new();

    public BufferingConsoleWriter(IConsoleWriter inner)
    {
        _inner = inner;
    }

    public void Write(string text)
    {
        _buffer.Enqueue(new BufferedWrite(BufferedOp.Write, text));
    }

    public void WriteLine(string text)
    {
        _buffer.Enqueue(new BufferedWrite(BufferedOp.WriteLine, text));
    }

    public void WriteLine()
    {
        _buffer.Enqueue(new BufferedWrite(BufferedOp.WriteLineEmpty, null));
    }

    public void WriteError(string text)
    {
        _buffer.Enqueue(new BufferedWrite(BufferedOp.WriteError, text));
    }

    public void Flush()
    {
        while (_buffer.TryDequeue(out var write))
        {
            switch (write.Op)
            {
                case BufferedOp.Write:
                    {
                        _inner.Write(write.Text!);
                        break;
                    }
                case BufferedOp.WriteLine:
                    {
                        _inner.WriteLine(write.Text!);
                        break;
                    }
                case BufferedOp.WriteLineEmpty:
                    {
                        _inner.WriteLine();
                        break;
                    }
                case BufferedOp.WriteError:
                    {
                        _inner.WriteError(write.Text!);
                        break;
                    }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_buffer.IsEmpty)
        {
            Flush();
        }

        return ValueTask.CompletedTask;
    }
}
