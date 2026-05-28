using System.Text;
using System.Text.Json;
using Vice.Concurrency;

namespace Vice.Logging;

public sealed class FileTelemetrySink : IAsyncDisposable
{
    public const string ConsentEnvVar = "VICE_TELEMETRY_CONSENT";

    private readonly string _path;
    private readonly SerialQueue _writer = new();
    private readonly bool _enabled;

    public FileTelemetrySink(Vice.Configuration.ViceDirectories dirs)
    {
        _path = Path.Combine(dirs.StateDir, "telemetry.jsonl");
        _enabled = IsConsentGiven();
    }

    public bool IsEnabled => _enabled;

    private static bool IsConsentGiven()
    {
        var raw = Environment.GetEnvironmentVariable(ConsentEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
    }

    public void Track(string eventName, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!_enabled)
        {
            return;
        }

        var payload = new Dictionary<string, string?>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["event"] = eventName,
        };
        if (properties is not null)
        {
            foreach (var kv in properties)
            {
                payload[kv.Key] = kv.Value;
            }
        }

        AppendLine(payload);
    }

    public void TrackException(Exception ex, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (!_enabled)
        {
            return;
        }

        var payload = new Dictionary<string, string?>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["event"] = "exception",
            ["type"] = ex.GetType().FullName,
            ["message"] = ex.Message,
        };
        if (properties is not null)
        {
            foreach (var kv in properties)
            {
                payload[kv.Key] = kv.Value;
            }
        }

        AppendLine(payload);
    }

    private void AppendLine(Dictionary<string, string?> payload)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, TelemetryJsonContext.Default.DictionaryStringString);
        }
        catch
        {
            return;
        }

        _ = _writer.EnqueueAsync(async _ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var options = new FileStreamOptions
                {
                    Mode = FileMode.Append,
                    Access = FileAccess.Write,
                };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                await using (var fs = new FileStream(_path, options))
                {
                    var bytes = Encoding.UTF8.GetBytes(json + "\n");
                    await fs.WriteAsync(bytes).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                }
                Vice.Persistence.FileAccessControl.RestrictToCurrentUser(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }, default);
    }

    public Task FlushAsync() => _writer.EnqueueAsync(_ => Task.CompletedTask, default);

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
