using System.Diagnostics;
using System.Net.Sockets;

namespace Vice.Mux.Sinks;

public static class SinkFactory
{
    public static ISink Open(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("sink spec is empty");
        }

        var colon = spec.IndexOf(':');
        var scheme = "file";
        var rest = spec;
        if (colon >= 0)
        {
            scheme = spec[..colon].ToLowerInvariant();
            rest = spec[(colon + 1)..];
        }

        return scheme switch
        {
            "file" => OpenFile(rest, append: false),
            "append" => OpenFile(rest, append: true),
            "tcp" => OpenTcp(rest),
            "exec" => OpenExec(rest),
            "pipe" => OpenPipe(rest),
            "null" => new NullSink(),
            _ => throw new ArgumentException($"unknown sink scheme '{scheme}' in '{spec}'"),
        };
    }

    private static ISink OpenFile(string path, bool append)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var mode = append ? FileMode.Append : FileMode.Create;
        var stream = new FileStream(full, mode, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
        return new StreamSink(stream, $"file:{full}");
    }

    private static ISink OpenTcp(string hostPort)
    {
        var bang = hostPort.LastIndexOf(':');
        if (bang < 0)
        {
            throw new ArgumentException($"tcp sink expects host:port, got '{hostPort}'");
        }

        var host = hostPort[..bang];
        if (!int.TryParse(hostPort[(bang + 1)..], out var port))
        {
            throw new ArgumentException($"tcp sink port not an int in '{hostPort}'");
        }

        var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            client.ConnectAsync(host, port, cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new TimeoutException($"tcp sink connect to {host}:{port} timed out");
        }
        client.NoDelay = true;
        return new TcpSink(client, $"tcp:{host}:{port}");
    }

    private static ISink OpenExec(string commandLine)
    {
        var parts = SplitCommand(commandLine);
        if (parts.Count == 0)
        {
            throw new ArgumentException("exec sink requires a command");
        }

        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
        };
        for (int i = 1; i < parts.Count; i++)
        {
            psi.ArgumentList.Add(parts[i]);
        }

        var proc = Process.Start(psi) ?? throw new InvalidOperationException($"exec sink failed to start '{commandLine}'");
        return new ProcessSink(proc, $"exec:{commandLine}");
    }

    private static ISink OpenPipe(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);
        return new StreamSink(stream, $"pipe:{path}");
    }

    private static List<string> SplitCommand(string line)
    {
        var result = new List<string>();
        var current = new List<char>();
        var inQuote = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (current.Count > 0)
                {
                    result.Add(new string(current.ToArray()));
                    current.Clear();
                }
                continue;
            }
            current.Add(c);
        }
        if (current.Count > 0)
        {
            result.Add(new string(current.ToArray()));
        }

        return result;
    }
}
