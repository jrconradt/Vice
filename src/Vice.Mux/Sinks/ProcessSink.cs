using System.Diagnostics;
using Vice.Logging;

namespace Vice.Mux.Sinks;

internal sealed class ProcessSink : ISink
{
    private readonly Process _process;
    private readonly Stream _stream;

    public ProcessSink(Process process, string label)
    {
        _process = process;
        _stream = process.StandardInput.BaseStream;
        Label = label;
    }

    public string Label { get; }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        => _stream.WriteAsync(chunk, ct);

    public ValueTask FlushAsync(CancellationToken ct)
        => new(_stream.FlushAsync(ct));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' final flush failed during dispose.",
                     ex);
        }
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' stream dispose failed.",
                     ex);
        }

        var gracefulExited = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            gracefulExited = true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
        }

        if (!gracefulExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx) when (killEx is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine(killEx);
            }

            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception waitEx) when (waitEx is TimeoutException or InvalidOperationException or OperationCanceledException)
            {
                Debug.WriteLine(waitEx);
            }
        }

        _process.Dispose();
    }
}
