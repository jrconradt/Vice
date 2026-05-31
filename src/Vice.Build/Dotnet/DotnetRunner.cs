using System.ComponentModel;
using System.Diagnostics;

namespace Vice.Build.Dotnet;

internal static class DotnetRunner
{
    internal static async Task<int> RunAsync(
        string executable,
        bool verbose,
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
            Vice.Output.Line($"{executable} {string.Join(' ', args)}");
        }

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            Vice.Output.Error($"{executable} not found on PATH: {ex.Message}");
            return 127;
        }
        catch (FileNotFoundException ex)
        {
            Vice.Output.Error($"{executable} not found on PATH: {ex.Message}");
            return 127;
        }

        if (started is null)
        {
            Vice.Output.Error("Failed to start dotnet process.");
            return 127;
        }

        using var proc = started;

        await using var killOnCancel = ct.Register(() => KillTree(proc));

        var stdoutTask = StreamLinesAsync(proc.StandardOutput, line => Vice.Output.Line(line));
        var stderrTask = StreamLinesAsync(proc.StandardError, line => Vice.Output.Error(line));

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
