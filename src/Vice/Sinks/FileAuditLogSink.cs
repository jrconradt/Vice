using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vice.Concurrency;
using Vice.Logging;
using Vice.Persistence;

namespace Vice;

public sealed partial class FileAuditLogSink : ILogSink, IAsyncDisposable
{
    private const string GENESIS_HASH = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly ViceLogLevel _minLevel;
    private readonly string _path;
    private readonly SerialQueue _queue;
    private long _sequence;
    private string _previousHash;

    public FileAuditLogSink(string path, ViceLogLevel minLevel = ViceLogLevel.Info)
    {
        _path = Path.GetFullPath(path);
        _minLevel = minLevel;
        _queue = new SerialQueue();
        _sequence = 0;
        _previousHash = GENESIS_HASH;
    }

    public bool IsEnabled(ViceLogLevel level) => level >= _minLevel;

    public void Log(ViceError error)
    {
        if (!IsEnabled(error.LogLevel))
        {
            return;
        }

        var level = error.LogLevel;
        var payload = LogFormat.Format(error);
        Append(level, payload);
    }

    public void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var fileName = file is null ? "?" : Path.GetFileName(file);
        var origin = $"{caller}@{fileName}:{line}";
        var payload = exception is null
            ? $"{origin}: {message}"
            : $"{origin}: {message} | {exception.GetType().Name}: {exception.Message}";
        Append(level, payload);
    }

    private void Append(ViceLogLevel level, string payload)
    {
        var timestamp = DateTimeOffset.UtcNow;
        _ = _queue.EnqueueAsync(ct => WriteRecordAsync(level, payload, timestamp, ct));
    }

    private async Task WriteRecordAsync(
        ViceLogLevel level,
        string payload,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var sequence = ++_sequence;
        var previousHash = _previousHash;
        var timestampText = timestamp.ToString("O", CultureInfo.InvariantCulture);
        var canonical = $"{previousHash}{sequence}{timestampText}{level}{payload}";
        var hash = ComputeHash(canonical);

        var record = new AuditRecord(
            sequence,
            timestampText,
            level.ToString(),
            payload,
            previousHash,
            hash);
        var line = JsonSerializer.Serialize(record, AuditRecordJsonContext.Default.AuditRecord) + "\n";

        await AtomicFile.AppendTextAsync(_path, line, ct).ConfigureAwait(false);
        FileAccessControl.RestrictToCurrentUser(_path);
        _previousHash = hash;
    }

    private static string ComputeHash(string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private sealed record AuditRecord(
        long seq,
        string ts,
        string level,
        string message,
        string prevHash,
        string hash);

    [JsonSerializable(typeof(AuditRecord))]
    private partial class AuditRecordJsonContext : JsonSerializerContext { }
}
