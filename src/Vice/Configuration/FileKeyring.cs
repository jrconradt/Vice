using System.Text.Json;
using Vice.Concurrency;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Configuration;

public sealed class FileKeyring : IKeyring
{
    public const string OptInEnvVar = "VICE_ALLOW_PLAINTEXT_KEYRING";

    private readonly string _path;
    private readonly SerialQueue _writeQueue = new();

    public FileKeyring(ViceDirectories dirs)
    {
        if (!IsOptedIn())
        {
            throw new InvalidOperationException(
                $"FileKeyring stores secrets as plaintext JSON at {dirs.DataDir}/keyring.json and is " +
                $"NOT suitable for production. To use it in dev/local set the env var " +
                $"{OptInEnvVar}=1 (or true/yes). For production provide an IKeyring implementation " +
                $"backed by a platform keychain (macOS Keychain, Windows Credential Manager, libsecret).");
        }

        _path = dirs.ResolveFileWithLegacy(dirs.DataDir, "keyring.json");
        HardenExisting(_path);
    }

    private static void HardenExisting(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = File.GetUnixFileMode(path);
            var leaked = mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            if (leaked != UnixFileMode.None)
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
                              $"keyring {path} had group/other access bits ({leaked}); tightening to owner-only 0600");
            }
        }

        FileAccessControl.RestrictToCurrentUser(path);
    }

    private static bool IsOptedIn()
    {
        var raw = Environment.GetEnvironmentVariable(OptInEnvVar);
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

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var dict = await ReadAsync(ct).ConfigureAwait(false);
        var hit = dict.TryGetValue(key, out var v);
        Audit("get", key, hit);
        return hit ? v : null;
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var dict = await ReadAsync(token).ConfigureAwait(false);
            dict[key] = value;
            await WriteAsync(dict, token).ConfigureAwait(false);
            Audit("set", key, true);
        }, ct);

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var dict = await ReadAsync(token).ConfigureAwait(false);
            var removed = dict.Remove(key);
            if (removed)
            {
                await WriteAsync(dict, token).ConfigureAwait(false);
            }

            Audit("delete", key, removed);
            return removed;
        }, ct);

    private static void Audit(string operation, string key, bool hit)
    {
        Vice.Log.Emit(ViceLogLevel.Info,
                      $"keyring {operation} key='{key}' result={(hit ? "hit" : "miss")} at {DateTimeOffset.UtcNow:o}");
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default)
    {
        var dict = await ReadAsync(ct).ConfigureAwait(false);
        return dict.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    public ValueTask DisposeAsync() => _writeQueue.DisposeAsync();

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken ct)
    {
        var json = await AtomicFile.ReadAllTextOrNullAsync(_path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize(json, KeyringJsonContext.Default.DictionaryStringString) ?? new();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Keyring at {_path} is corrupt and cannot be parsed; refusing to proceed to avoid overwriting stored secrets. Inspect or remove the file manually.", ex);
        }
    }

    private async Task WriteAsync(Dictionary<string, string> dict, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        else
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(dict, KeyringJsonContext.Default.DictionaryStringString);
        await AtomicFile.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
    }
}
