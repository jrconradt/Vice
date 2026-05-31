using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vice.Configuration;
using Vice.Persistence;

namespace Vice.Net.Research;

internal sealed class ResearchCache
{
    private const string ResearchSubdir = "research";

    private static readonly TimeSpan SearchTtl = TimeSpan.FromHours(1);

    private static readonly TimeSpan ContentMaxAge = TimeSpan.FromDays(30);

    private const long DefaultContentBudgetBytes = 512L * 1024 * 1024;

    private readonly string _root;

    private readonly long _contentBudgetBytes;

    public ResearchCache()
        : this(ResolveBudget())
    {
    }

    public ResearchCache(long contentBudgetBytes)
    {
        _root = Path.Combine(new ViceDirectories("vice").CacheDir, ResearchSubdir);
        _contentBudgetBytes = contentBudgetBytes > 0 ? contentBudgetBytes : DefaultContentBudgetBytes;
    }

    public async Task<IReadOnlyList<SearchHit>?> ReadSearchAsync(string source,
                                                                string cacheKey,
                                                                CancellationToken ct)
    {
        var path = SearchPath(source, cacheKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        if (age > SearchTtl)
        {
            TryDelete(path);
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var hits = await JsonSerializer.DeserializeAsync(stream,
                                                             ResearchJsonContext.Default.ListSearchHit,
                                                             ct).ConfigureAwait(false);
            return hits;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task WriteSearchAsync(string source,
                                       string cacheKey,
                                       IReadOnlyList<SearchHit> hits,
                                       CancellationToken ct)
    {
        var path = SearchPath(source, cacheKey);
        var list = hits as List<SearchHit> ?? hits.ToList();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(list,
                                                        ResearchJsonContext.Default.ListSearchHit);
        try
        {
            await AtomicFile.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        PruneSearch();
    }

    public string? ReadContentPath(string source,
                                   string id,
                                   string? format,
                                   string extension)
    {
        var path = ContentPath(source, id, format, extension);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length == 0)
        {
            return null;
        }

        TouchAccess(path);
        return path;
    }

    public async Task WriteContentAsync(string source,
                                        string id,
                                        string? format,
                                        string extension,
                                        byte[] bytes,
                                        CancellationToken ct)
    {
        var path = ContentPath(source, id, format, extension);
        try
        {
            await AtomicFile.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            TouchAccess(path);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        PruneContent();
    }

    public void Prune()
    {
        PruneSearch();
        PruneContent();
    }

    public static string ComputeKey(string query,
                                    int limit,
                                    int offset,
                                    string? format = null)
    {
        var material = format is null
            ? $"{query} {limit} {offset}"
            : $"{query} {limit} {offset} {format}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexStringLower(hash);
    }

    private void PruneSearch()
    {
        var cutoff = DateTime.UtcNow - SearchTtl;
        foreach (var file in EnumerateLeaf("search"))
        {
            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                continue;
            }

            if (written < cutoff)
            {
                TryDelete(file);
            }
        }
    }

    private void PruneContent()
    {
        var entries = new List<ContentEntry>();
        var cutoff = DateTime.UtcNow - ContentMaxAge;
        var total = 0L;
        foreach (var file in EnumerateLeaf("content"))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                continue;
            }

            if (!info.Exists)
            {
                continue;
            }

            if (info.LastAccessTimeUtc < cutoff)
            {
                TryDelete(file);
                continue;
            }

            total += info.Length;
            entries.Add(new ContentEntry(file, info.Length, info.LastAccessTimeUtc));
        }

        if (total <= _contentBudgetBytes)
        {
            return;
        }

        entries.Sort((a, b) => a.LastAccessUtc.CompareTo(b.LastAccessUtc));
        var index = 0;
        while (total > _contentBudgetBytes
            && index < entries.Count)
        {
            var entry = entries[index];
            TryDelete(entry.Path);
            total -= entry.Length;
            index++;
        }
    }

    private IEnumerable<string> EnumerateLeaf(string leaf)
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        var sources = new List<string>();
        try
        {
            sources.AddRange(Directory.EnumerateDirectories(_root));
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            yield break;
        }

        foreach (var sourceDir in sources)
        {
            var leafDir = Path.Combine(sourceDir, leaf);
            if (!Directory.Exists(leafDir))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(leafDir);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                List<string> files;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(current))
                    {
                        pending.Push(sub);
                    }

                    files = Directory.EnumerateFiles(current).ToList();
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    continue;
                }

                foreach (var file in files)
                {
                    if (file.EndsWith(".lock", StringComparison.Ordinal)
                        || file.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return file;
                }
            }
        }
    }

    private string SearchPath(string source,
                              string cacheKey)
    {
        return Path.Combine(_root, source, "search", $"{cacheKey}.json");
    }

    private string ContentPath(string source,
                               string id,
                               string? format,
                               string extension)
    {
        var name = string.IsNullOrEmpty(format)
            ? $"{Sanitize(id)}.{extension}"
            : $"{Sanitize(id)}.{Sanitize(format)}.{extension}";
        return Path.Combine(_root,
                            source,
                            "content",
                            name);
    }

    private static long ResolveBudget()
    {
        var raw = Environment.GetEnvironmentVariable("VICE_CACHE_MAX_BYTES");
        if (raw is not null
            && long.TryParse(raw, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return DefaultContentBudgetBytes;
    }

    private static void TouchAccess(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static string Sanitize(string id)
    {
        var chars = new List<char>(id.Length);
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c) || c == '-'
                || c == '_'
                || c == '.')
            {
                chars.Add(c);
            }
            else
            {
                chars.Add('_');
            }
        }

        return new string(chars.ToArray());
    }

    private readonly record struct ContentEntry(string Path,
                                                long Length,
                                                DateTime LastAccessUtc);
}
