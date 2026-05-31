using System.Text;

namespace Vice;

internal sealed class ConsoleOutputSink : IOutputSink, IDisposable
{
    private const int BufferBytes = 1 << 16;

    private readonly StreamWriter _writer;
    private bool _disposed;

    public ConsoleOutputSink()
    {
        var stdout = System.Console.OpenStandardOutput();
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _writer = new StreamWriter(stdout, encoding, BufferBytes)
        {
            AutoFlush = false,
        };
    }

    public void Line(string text)
    {
        _writer.Write(text);
        _writer.Write(_writer.NewLine);
    }

    public void Line()
    {
        _writer.Write(_writer.NewLine);
    }

    public void Write(string text)
    {
        _writer.Write(text);
    }

    public void Error(string text)
    {
        _writer.Flush();
        System.Console.Error.WriteLine(text);
    }

    public void Flush()
    {
        if (_disposed)
        {
            return;
        }

        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _writer.Flush();
        }
        finally
        {
            _writer.Dispose();
        }
    }
}
