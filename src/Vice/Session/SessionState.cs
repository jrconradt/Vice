using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Vice.Concurrency;
using Vice.Configuration;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Session;

public sealed class SessionState : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

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
        PipeName = pipeName ?? $"{name}-session-{Environment.UserName}";

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
            return n;
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

    [RequiresUnreferencedCode("Generic config deserialize uses reflection over T")]
    [RequiresDynamicCode("Generic config deserialize uses reflection over T")]
    public async Task<T> GetConfigAsync<T>(string key, T defaultValue, CancellationToken ct)
    {
        var config = await LoadConfigAsync(ct).ConfigureAwait(false);
        if (!config.TryGetValue(key, out var element))
        {
            return defaultValue;
        }

        try
        {
            var value = element.Deserialize<T>(JsonOptions);
            return value ?? defaultValue;
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Debug, $"config deserialize miss for key '{key}'", ex);
            return defaultValue;
        }
    }

    [RequiresUnreferencedCode("Generic config serialize uses reflection over T")]
    [RequiresDynamicCode("Generic config serialize uses reflection over T")]
    public Task SetConfigAsync<T>(string key, T value, CancellationToken ct)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var config = await LoadConfigAsync(token).ConfigureAwait(false);
            var serialized = JsonSerializer.SerializeToElement(value, JsonOptions);
            config[key] = serialized;
            await SaveConfigAsync(config, token).ConfigureAwait(false);
        }, ct);

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
            Vice.Log.Emit(ViceLogLevel.Warn, $"config.json corrupt at {ConfigPath}, returning empty", ex);
            return new Dictionary<string, JsonElement>();
        }
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
