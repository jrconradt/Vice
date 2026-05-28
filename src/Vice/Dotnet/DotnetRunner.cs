using System.ComponentModel;
using System.Diagnostics;

namespace Vice.Dotnet;

internal static class DotnetRunner
{
    internal static async Task<int> RunAsync(
        bool verbose,
        CancellationToken ct,
        params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
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
            Vice.Output.Line($"dotnet {string.Join(' ', args)}");
        }

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            Vice.Output.Error($"dotnet not found on PATH: {ex.Message}");
            return 127;
        }
        catch (FileNotFoundException ex)
        {
            Vice.Output.Error($"dotnet not found on PATH: {ex.Message}");
            return 127;
        }

        if (started is null)
        {
            Vice.Output.Error("Failed to start dotnet process.");
            return 127;
        }

        using var proc = started;

        await using var cancelReg = ct.Register(() =>
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
        });

        var stdoutTask = StreamLinesAsync(proc.StandardOutput, line => Vice.Output.Line(line), ct);
        var stderrTask = StreamLinesAsync(proc.StandardError, line => Vice.Output.Error(line), ct);

        await Task.WhenAll(stdoutTask, stderrTask);
        await proc.WaitForExitAsync(ct);

        return proc.ExitCode;
    }

    private static async Task StreamLinesAsync(
        System.IO.StreamReader reader,
        Action<string> write,
        CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            write(line);
        }
    }
}
