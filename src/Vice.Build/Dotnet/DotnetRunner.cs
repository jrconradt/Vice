using System.ComponentModel;
using System.Diagnostics;
using Vice.Display.Rendering;

namespace Vice.Build.Dotnet;

internal static class DotnetRunner
{
    internal static async Task<int> RunAsync(
        string executable,
        bool verbose,
        IConsoleWriter console,
        CancellationToken ct,
        params string[] args)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (verbose)
        {
            console.WriteLine($"{executable} {string.Join(' ', args)}");
        }

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            console.WriteError($"{executable} not found on PATH: {ex.Message}");
            return 127;
        }
        catch (FileNotFoundException ex)
        {
            console.WriteError($"{executable} not found on PATH: {ex.Message}");
            return 127;
        }

        if (started is null)
        {
            console.WriteError("Failed to start dotnet process.");
            return 127;
        }

        using var proc = started;

        await using var killOnCancel = ct.Register(() => KillTree(proc));

        var stdoutTask = StreamLinesAsync(proc.StandardOutput, line => console.WriteLine(line));
        var stderrTask = StreamLinesAsync(proc.StandardError, line => console.WriteError(line));

        await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        return proc.ExitCode;
    }

    private static void KillTree(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static async Task StreamLinesAsync(System.IO.StreamReader reader, Action<string> write)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            write(line);
        }
    }
}
