using System.Diagnostics;

namespace Vice.Mux.Sinks;

internal sealed class ProcessSink : SinkBase
{
    private readonly Process _process;

    public ProcessSink(Process process, string label)
        : base(process.StandardInput.BaseStream, label)
    {
        _process = process;
    }

    protected override async ValueTask DisposeCoreAsync()
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
            System.Diagnostics.Debug.WriteLine(ex);
        }

        if (!gracefulExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx) when (killEx is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine(killEx);
            }

            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception waitEx) when (waitEx is TimeoutException or InvalidOperationException or OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(waitEx);
            }
        }

        _process.Dispose();
    }
}
