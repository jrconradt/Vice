namespace Vice.Persistence;

public static class SafeWriteRoots
{
    private const int CacheTtlSeconds = 30;
    private static readonly object _rootsLock = new();
    private static IReadOnlyList<string>? _rootsCache;
    private static string? _rootsCacheKey;
    private static DateTime _rootsExpiresAtUtc;

    public static bool IsAllowed(string fullPath, out string reason)
    {
        reason = "";
        string canonical;
        try
        {
            canonical = CanonicalisePath(fullPath);
        }
        catch (Exception ex)
        {
            reason = $"invalid path ({ex.Message})";
            return false;
        }

        var roots = GetCachedRoots();
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            var rooted = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (string.Equals(canonical, root, StringComparison.Ordinal)
                || canonical.StartsWith(rooted, StringComparison.Ordinal))
            {
                return true;
            }
        }

        reason = $"outside allowed roots ({string.Join(", ", roots)})";
        return false;
    }

    private static IReadOnlyList<string> GetCachedRoots()
    {
        var key = ComputeRootsCacheKey();
        var nowUtc = DateTime.UtcNow;
        var cached = Volatile.Read(ref _rootsCache);
        var cachedKey = Volatile.Read(ref _rootsCacheKey);
        if (cached is not null
            && string.Equals(cachedKey, key, StringComparison.Ordinal)
            && nowUtc < _rootsExpiresAtUtc)
        {
            return cached;
        }

        lock (_rootsLock)
        {
            if (_rootsCache is not null
                && string.Equals(_rootsCacheKey, key, StringComparison.Ordinal)
                && DateTime.UtcNow < _rootsExpiresAtUtc)
            {
                return _rootsCache;
            }

            var fresh = CollectRoots();
            _rootsExpiresAtUtc = DateTime.UtcNow.AddSeconds(CacheTtlSeconds);
            Volatile.Write(ref _rootsCacheKey, key);
            Volatile.Write(ref _rootsCache, fresh);
            return fresh;
        }
    }

    private static string ComputeRootsCacheKey()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "";
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? "";
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? "";
        var tmpdir = Environment.GetEnvironmentVariable("TMPDIR") ?? "";
        var allowed = Environment.GetEnvironmentVariable("VICE_ALLOWED_ROOTS") ?? "";
        var cwd = Environment.CurrentDirectory ?? "";
        return $"{home}|{xdgData}|{xdgCache}|{xdgState}|{tmpdir}|{allowed}|{cwd}";
    }

    private static List<string> CollectRoots()
    {
        var raw = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
            Environment.GetEnvironmentVariable("TMPDIR"),
            Path.GetTempPath(),
            Environment.CurrentDirectory,
        };

        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in raw)
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            string canonical;
            try
            {
                canonical = CanonicalisePath(entry);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or IOException or System.Security.SecurityException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                continue;
            }

            if (seen.Add(canonical))
            {
                resolved.Add(canonical);
            }
        }

        return resolved;
    }

    private static string CanonicalisePath(string raw)
    {
        var full = Path.GetFullPath(raw);
        if (Directory.Exists(full))
        {
            try
            {
                var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
                if (target is not null && !string.IsNullOrEmpty(target.FullName))
                {
                    full = Path.GetFullPath(target.FullName);
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return full;
        }

        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent)
            && Directory.Exists(parent))
        {
            try
            {
                var target = File.ResolveLinkTarget(parent, returnFinalTarget: true);
                if (target is not null && !string.IsNullOrEmpty(target.FullName))
                {
                    var resolvedParent = Path.GetFullPath(target.FullName);
                    full = Path.Combine(resolvedParent, Path.GetFileName(full));
                }
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

        return full;
    }
}
