using System.Diagnostics;
using System.Reflection;

namespace Vice.Cli.Tests;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr);

public static class CliProcess
{
    public static string ViceCliDll { get; } = ResolveDll("ViceCliDll");

    public static string ViceMuxCliDll { get; } = ResolveDll("ViceMuxCliDll");

    private static string ResolveDll(string metadataKey)
    {
        foreach (var attr in typeof(CliProcess).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, metadataKey, StringComparison.Ordinal))
            {
                return Path.GetFullPath(attr.Value ?? string.Empty);
            }
        }

        throw new InvalidOperationException($"assembly metadata '{metadataKey}' was not emitted; the CLI project reference is misconfigured.");
    }

    public static async Task<CliResult> RunAsync(
        string dll,
        string[] args,
        string stdin = "",
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException($"failed to launch '{dll}'.");
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new CliResult(process.ExitCode, stdout, stderr);
        }
    }

    public static async Task<CliResult> RunWithEarlyStdoutCloseAsync(
        string dll,
        string[] args,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException($"failed to launch '{dll}'.");
        }

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            process.StandardInput.Close();

            var firstByte = process.StandardOutput.BaseStream.ReadByte();
            process.StandardOutput.Close();

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var partial = firstByte < 0 ? string.Empty : $"{(char)firstByte}";
            return new CliResult(process.ExitCode, partial, stderr);
        }
    }
}
