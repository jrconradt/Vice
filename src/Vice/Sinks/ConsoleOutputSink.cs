using System.Text;
using Vice.Display.Rendering;

namespace Vice;

internal sealed class ConsoleOutputSink : IOutputSink, IDisposable
{
    private const int BUFFER_BYTES = 1 << 16;

    private readonly StreamWriter _writer;
    private readonly bool _sanitize;
    private bool _disposed;

    public ConsoleOutputSink()
    {
        var stdout = System.Console.OpenStandardOutput();
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _sanitize = !System.Console.IsOutputRedirected;
        _writer = new StreamWriter(stdout, encoding, BUFFER_BYTES)
        {
            AutoFlush = false,
        };
    }

    public void Line(string text)
    {
        _writer.Write(_sanitize ? AnsiStripper.Strip(text) : text);
        _writer.Write(_writer.NewLine);
    }

    public void Line()
    {
        _writer.Write(_writer.NewLine);
    }

    public void Write(string text)
    {
        _writer.Write(_sanitize ? AnsiStripper.Strip(text) : text);
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
