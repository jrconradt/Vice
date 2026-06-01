using System.Diagnostics;
using Vice.Logging;

namespace Vice.Mux.Sinks;

internal sealed class ProcessSink : StreamBackedSink
{
    private readonly Process _process;

    public ProcessSink(Process process, string label)
        : base(process.StandardInput.BaseStream, label)
    {
        _process = process;
    }

    protected override async ValueTask DisposeUnderlyingAsync()
    {
        var gracefulExited = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            gracefulExited = true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            Vice.Quietly.Swallow(ex);
        }

        if (!gracefulExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx) when (killEx is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Vice.Quietly.Swallow(killEx);
            }

            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception waitEx) when (waitEx is TimeoutException or InvalidOperationException or OperationCanceledException)
            {
                Vice.Quietly.Swallow(waitEx);
            }
        }

        ReportExit(gracefulExited);
        _process.Dispose();
    }

    private void ReportExit(bool gracefulExited)
    {
        if (!gracefulExited)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' downstream process did not exit within the grace period and was killed.");
            return;
        }

        int exitCode;
        try
        {
            exitCode = _process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Vice.Quietly.Swallow(ex);
            return;
        }

        if (exitCode != 0)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' downstream process exited with code {exitCode}.");
        }
    }
}
