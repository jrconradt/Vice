using System.Diagnostics;
using Vice.Execution;

namespace Vice.Display;

public sealed class PagerSession : IAsyncDisposable, IDisposable
{
    private readonly Process? _process;
    public TextWriter Writer { get; }

    private PagerSession(Process? process, TextWriter writer)
    {
        _process = process;
        Writer = writer;
    }

    public bool IsActive => _process is not null;

    public static PagerSession Open(ICommandContext ctx)
    {
        if (ctx.NoPager)
        {
            return Disabled();
        }

        if (!IsInteractiveOutput())
        {
            return Disabled();
        }

        var (file, args) = ResolvePagerCommand();
        if (file is null)
        {
            return Disabled();
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            var process = Process.Start(psi);
            if (process is null)
            {
                return Disabled();
            }

            return new PagerSession(process, process.StandardInput);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Quietly.Swallow(ex);
            return Disabled();
        }
    }

    private static PagerSession Disabled() => new(null, Console.Out);

    private static bool IsInteractiveOutput()
    {
        try
        {
            return !Console.IsOutputRedirected;
        }
        catch (IOException ex)
        {
            Quietly.Swallow(ex);
            return false;
        }
    }

    private static (string? File, IReadOnlyList<string> Args) ResolvePagerCommand()
    {
        var envPager = Environment.GetEnvironmentVariable("PAGER");
        if (!string.IsNullOrWhiteSpace(envPager))
        {
            var parts = SplitCommand(envPager);
            if (parts.Count > 0)
            {
                return (parts[0], parts.Skip(1).ToList());
            }
        }
        if (FindOnPath("less") is { } less)
        {
            return (less, new[] { "-R" });
        }

        return (null, Array.Empty<string>());
    }

    private static IReadOnlyList<string> SplitCommand(string s)
    {
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(dir, fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            Writer.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Quietly.Swallow(ex);
        }
        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Quietly.Swallow(ex);
        }
        var gracefulExited = false;
        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            gracefulExited = true;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Quietly.Swallow(ex);
        }

        if (!gracefulExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx) when (killEx is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Quietly.Swallow(killEx);
            }

            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception waitEx) when (waitEx is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Quietly.Swallow(waitEx);
            }
        }
        try
        {
            _process.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Quietly.Swallow(ex);
        }
    }
}
