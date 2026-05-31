using System.Text.Json;
using Vice.Concurrency;
using Vice.Persistence;

namespace Vice.Configuration;

public sealed class SourcesFile : IAsyncDisposable
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _path;
    private readonly SerialQueue _writeQueue = new();

    public SourcesFile(ViceDirectories dirs)
    {
        _path = Path.Combine(dirs.ConfigDir, "sources.json");
    }

    public Task<IReadOnlyList<string>> ReadAsync(CancellationToken ct = default) => LoadAsync(ct);

    public Task AddAsync(string source, CancellationToken ct = default)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var current = await LoadAsync(token).ConfigureAwait(false);
            if (!current.Contains(source, StringComparer.Ordinal))
            {
                var next = new List<string>(current)
                {
                    source,
                };
                await SaveAsync(next, token).ConfigureAwait(false);
            }
        }, ct);

    public Task<bool> RemoveAsync(string source, CancellationToken ct = default)
        => _writeQueue.EnqueueAsync(async token =>
        {
            var current = await LoadAsync(token).ConfigureAwait(false);
            var next = current.Where(s => !string.Equals(s, source, StringComparison.Ordinal)).ToList();
            if (next.Count == current.Count)
            {
                return false;
            }

            await SaveAsync(next, token).ConfigureAwait(false);
            return true;
        }, ct);

    public Task ReplaceAsync(IEnumerable<string> sources, CancellationToken ct = default)
        => _writeQueue.EnqueueAsync(token => SaveAsync(Dedup(sources), token), ct);

    public ValueTask DisposeAsync() => _writeQueue.DisposeAsync();

    private async Task<IReadOnlyList<string>> LoadAsync(CancellationToken ct)
    {
        var json = await AtomicFile.ReadAllTextOrNullAsync(_path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var model = JsonSerializer.Deserialize(json, SourcesJsonContext.Default.SourceList);
            if (model is null)
            {
                return Array.Empty<string>();
            }

            return Dedup(Migrate(model).Sources);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static SourceList Migrate(SourceList model) => model.SchemaVersion switch
    {
        <= CurrentSchemaVersion => model with { SchemaVersion = CurrentSchemaVersion },
        _ => model,
    };

    private async Task SaveAsync(IReadOnlyList<string> sources, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var model = new SourceList
        {
            SchemaVersion = CurrentSchemaVersion,
            Sources = sources,
        };
        var json = JsonSerializer.Serialize(model, SourcesJsonContext.Default.SourceList);
        await AtomicFile.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> Dedup(IEnumerable<string> sources)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var s in sources)
        {
            if (!string.IsNullOrWhiteSpace(s)
                && seen.Add(s))
            {
                result.Add(s);
            }
        }

        return result;
    }
}
