using System.Diagnostics;
using System.Net.Sockets;
using Vice.Logging;

namespace Vice.Mux.Sinks;

public static class SinkFactory
{
    public static ISink Open(string spec, IViceLogger logger)
    {
        var (scheme, rest) = Split(spec);
        return scheme switch
        {
            "file" => OpenFile(rest, append: false, logger),
            "append" => OpenFile(rest, append: true, logger),
            "tcp" => OpenTcp(rest, logger),
            "exec" => OpenExec(rest, logger),
            "pipe" => OpenPipe(rest, logger),
            "null" => new NullSink(),
            _ => throw new ArgumentException($"unknown sink scheme '{scheme}' in '{spec}'"),
        };
    }

    public static async ValueTask<ISink> OpenAsync(string spec, CancellationToken ct, IViceLogger logger)
    {
        var (scheme, rest) = Split(spec);
        return scheme switch
        {
            "file" => OpenFile(rest, append: false, logger),
            "append" => OpenFile(rest, append: true, logger),
            "tcp" => await OpenTcpAsync(rest, ct, logger),
            "exec" => OpenExec(rest, logger),
            "pipe" => OpenPipe(rest, logger),
            "null" => new NullSink(),
            _ => throw new ArgumentException($"unknown sink scheme '{scheme}' in '{spec}'"),
        };
    }

    private static (string scheme, string rest) Split(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("sink spec is empty");
        }

        var colon = spec.IndexOf(':');
        if (colon < 0)
        {
            return ("file", spec);
        }

        return (spec[..colon].ToLowerInvariant(), spec[(colon + 1)..]);
    }

    private static ISink OpenFile(string path, bool append, IViceLogger logger)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var mode = append ? FileMode.Append : FileMode.Create;
        var stream = new FileStream(full, mode, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
        return new StreamSink(stream, $"file:{full}", logger);
    }

    private static ISink OpenTcp(string hostPort, IViceLogger logger)
    {
        var (host, port) = ParseEndpoint(hostPort);
        var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            client.ConnectAsync(host,
                                port,
                                cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new TimeoutException($"tcp sink connect to {host}:{port} timed out");
        }

        client.NoDelay = true;
        return new TcpSink(client, $"tcp:{host}:{port}", logger);
    }

    private static async ValueTask<ISink> OpenTcpAsync(string hostPort, CancellationToken ct, IViceLogger logger)
    {
        var (host, port) = ParseEndpoint(hostPort);
        var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(host,
                                      port,
                                      cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"tcp sink connect to {host}:{port} timed out");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        client.NoDelay = true;
        return new TcpSink(client, $"tcp:{host}:{port}", logger);
    }

    private static (string host, int port) ParseEndpoint(string hostPort)
    {
        var bang = hostPort.LastIndexOf(':');
        if (bang < 0)
        {
            throw new ArgumentException($"Invalid endpoint '{hostPort}'. Expected format: host:port");
        }

        var host = hostPort[..bang];
        if (!int.TryParse(hostPort[(bang + 1)..], out var port)
            || port < 1
            || port > 65535)
        {
            throw new ArgumentException($"Invalid endpoint '{hostPort}'. Expected format: host:port");
        }

        return (host, port);
    }

    private static ISink OpenExec(string commandLine, IViceLogger logger)
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
        return new ProcessSink(proc, $"exec:{commandLine}", logger);
    }

    private static ISink OpenPipe(string path, IViceLogger logger)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);
        return new StreamSink(stream, $"pipe:{path}", logger);
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
