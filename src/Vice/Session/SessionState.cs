using System.Text.Json;
using Vice.Concurrency;
using Vice.Configuration;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Session;

public sealed class SessionState : IAsyncDisposable
{
    public const int ProtocolVersion = 1;

    private readonly SerialQueue _writeQueue = new();
    private int _disposed;

    public ViceDirectories Dirs { get; }
    public string BasePath => Dirs.StateDir;
    public string HistoryPath { get; }
    public string JobsPath { get; }
    public string ConfigPath { get; }
    public string PipeName { get; }

    public SessionState(string? basePath = null, string? pipeName = null, IViceLogger? logger = null)
        : this(BuildDirsForLegacyCtor(basePath, "vice"), pipeName, logger, appName: "vice")
    {
    }

    public SessionState(ViceDirectories dirs, string? pipeName = null, IViceLogger? logger = null, string? appName = null)
    {
        _ = logger;
        Dirs = dirs ?? throw new ArgumentNullException(nameof(dirs));
        var name = appName ?? dirs.AppName;
        PipeName = pipeName ?? $"{name}-session-v{ProtocolVersion}-{Environment.UserName}";

        HistoryPath = Dirs.ResolveFileWithLegacy(Dirs.StateDir, "history");
        JobsPath = Dirs.ResolveFileWithLegacy(Dirs.DataDir, "jobs.json");
        ConfigPath = Dirs.ResolveFileWithLegacy(Dirs.ConfigDir, "config.json");
    }

    public static SessionState For(
        string appName,
        string? basePath = null,
        string? pipeName = null,
        IViceLogger? logger = null)
        => new(BuildDirsForLegacyCtor(basePath, appName), pipeName, logger, appName);

    private static ViceDirectories BuildDirsForLegacyCtor(string? basePath, string appName)
        => basePath is null
            ? new ViceDirectories(appName)
            : ViceDirectories.UnifiedAt(appName, basePath);

    public async Task<int> GetConcurrencyAsync(CancellationToken ct)
    {
        var config = await LoadConfigAsync(ct).ConfigureAwait(false);
        if (!config.TryGetValue("concurrency", out var element))
        {
            return 3;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n))
        {
            return Math.Clamp(n, 1, Environment.ProcessorCount * 4);
        }

        return 3;
    }

    public Task SetConcurrencyAsync(int value, CancellationToken ct)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var config = await LoadConfigAsync(token).ConfigureAwait(false);
            config["concurrency"] = JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
            await SaveConfigAsync(config, token).ConfigureAwait(false);
        }, ct);

    public Task SetStringConfigAsync(string key, string value, CancellationToken ct)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var config = await LoadConfigAsync(token).ConfigureAwait(false);
            var encoded = JsonSerializer.Serialize(value, SessionStateJsonContext.Default.String);
            config[key] = JsonDocument.Parse(encoded).RootElement.Clone();
            await SaveConfigAsync(config, token).ConfigureAwait(false);
        }, ct);

    public async Task<int> GetConfigAsync(string key, int defaultValue, CancellationToken ct)
    {
        var config = await LoadConfigAsync(ct).ConfigureAwait(false);
        if (!config.TryGetValue(key, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n))
        {
            return n;
        }

        return defaultValue;
    }

    public async Task<string> GetConfigAsync(string key, string defaultValue, CancellationToken ct)
    {
        var config = await LoadConfigAsync(ct).ConfigureAwait(false);
        if (!config.TryGetValue(key, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } s)
        {
            return s;
        }

        return defaultValue;
    }

    public Task SetConfigAsync(string key, int value, CancellationToken ct)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var config = await LoadConfigAsync(token).ConfigureAwait(false);
            config[key] = JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
            await SaveConfigAsync(config, token).ConfigureAwait(false);
        }, ct);

    public Task SetConfigAsync(string key, string value, CancellationToken ct)
        => SetStringConfigAsync(key, value, ct);

    private async Task<Dictionary<string, JsonElement>> LoadConfigAsync(CancellationToken ct)
    {
        var json = await AtomicFile.ReadAllTextOrNullAsync(ConfigPath, ct).ConfigureAwait(false);
        if (json is null)
        {
            Vice.Log.Emit(ViceLogLevel.Info, "config not loaded, using defaults");
            return new Dictionary<string, JsonElement>();
        }
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, JsonElement>();
        }

        try
        {
            return JsonSerializer.Deserialize(json, SessionStateJsonContext.Default.DictionaryStringJsonElement)
                ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException ex)
        {
            await QuarantineCorruptConfigAsync(json, ex, ct).ConfigureAwait(false);
            return new Dictionary<string, JsonElement>();
        }
    }

    private async Task QuarantineCorruptConfigAsync(string payload, Exception ex, CancellationToken ct)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var corruptPath = $"{ConfigPath}.corrupt-{stamp}";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var fs = new FileStream(corruptPath, options))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            Vice.Persistence.FileAccessControl.RestrictToCurrentUser(corruptPath);
        }
        catch (Exception writeEx)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"failed to quarantine corrupt config.json to {corruptPath}", writeEx);
        }
        Vice.Log.Emit(ViceLogLevel.Warn,
                      $"config.json corrupt at {ConfigPath}, returning empty (quarantined to {corruptPath})",
                      ex);
    }

    private async Task SaveConfigAsync(Dictionary<string, JsonElement> config, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var json = JsonSerializer.Serialize(config, SessionStateJsonContext.Default.DictionaryStringJsonElement);
        await AtomicFile.WriteAllTextAsync(ConfigPath, json, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _writeQueue.DisposeAsync().ConfigureAwait(false);
    }
}
