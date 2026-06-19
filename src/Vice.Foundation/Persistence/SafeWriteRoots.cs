using Vice.Logging;

namespace Vice.Persistence;

public static class SafeWriteRoots
{
    public static bool IsAllowed(string fullPath, out string reason, IViceLogger? logger = null)
        => IsAllowed(fullPath, out _, out reason, logger);

    public static bool IsAllowed(
        string fullPath,
        out string canonical,
        out string reason,
        IViceLogger? logger = null)
    {
        var sink = logger ?? NullViceLogger.Instance;
        reason = "";
        canonical = fullPath;
        try
        {
            canonical = CanonicalisePath(fullPath, sink);
        }
        catch (Exception ex)
        {
            reason = $"invalid path ({ex.Message})";
            sink.Log(ViceLogLevel.Warn,
                     $"safe-write-root denied: requested='{fullPath}' reason='{reason}'");
            return false;
        }

        var roots = CollectRoots(sink);
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
        sink.Log(ViceLogLevel.Warn,
                 $"safe-write-root denied: requested='{fullPath}' canonical='{canonical}' reason='{reason}'");
        return false;
    }

    private static IEnumerable<string?> RootSources()
    {
        yield return Directory.GetCurrentDirectory();
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        yield return Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        yield return Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        yield return Environment.GetEnvironmentVariable("TMPDIR");
        yield return Path.GetTempPath();
        var allowed = Environment.GetEnvironmentVariable("VICE_ALLOWED_ROOTS");
        if (!string.IsNullOrEmpty(allowed))
        {
            foreach (var entry in allowed.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return entry;
            }
        }
    }

    private static List<string> CollectRoots(IViceLogger logger)
    {
        var raw = RootSources();

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
                canonical = CanonicalisePath(entry, logger);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or IOException or System.Security.SecurityException)
            {
                Quietly.Swallow(ex, logger);
                continue;
            }

            if (string.Equals(canonical, Path.GetPathRoot(canonical), StringComparison.Ordinal))
            {
                logger.Log(ViceLogLevel.Warn,
                           $"safe-write-root refused over-broad volume root: root='{canonical}'");
                continue;
            }

            if (seen.Add(canonical))
            {
                resolved.Add(canonical);
            }
        }

        logger.Log(ViceLogLevel.Info,
                   $"safe-write-root active roots: {string.Join(", ", resolved)}");
        return resolved;
    }

    private const int MAX_LINK_HOPS = 256;

    private static string CanonicalisePath(string raw, IViceLogger logger)
    {
        var full = Path.GetFullPath(raw);
        var root = Path.GetPathRoot(full) ?? "";
        if (string.IsNullOrEmpty(root))
        {
            root = Path.DirectorySeparatorChar.ToString();
        }

        var pending = new Stack<string>();
        PushComponents(pending, full, root);

        var resolved = root;
        var hops = 0;
        while (pending.Count > 0)
        {
            var component = pending.Pop();
            var candidate = Path.Combine(resolved, component);

            FileSystemInfo? target;
            try
            {
                target = File.ResolveLinkTarget(candidate, returnFinalTarget: false);
            }
            catch (IOException ex)
            {
                Quietly.Swallow(ex, logger);
                resolved = candidate;
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                Quietly.Swallow(ex, logger);
                resolved = candidate;
                continue;
            }

            if (target is null || string.IsNullOrEmpty(target.FullName))
            {
                resolved = candidate;
                continue;
            }

            hops++;
            if (hops > MAX_LINK_HOPS)
            {
                throw new IOException($"symlink chain exceeded {MAX_LINK_HOPS} hops resolving '{full}'");
            }

            var linkTarget = target.FullName;
            var absolute = Path.IsPathRooted(linkTarget)
                ? Path.GetFullPath(linkTarget)
                : Path.GetFullPath(Path.Combine(resolved, linkTarget));
            resolved = Path.GetPathRoot(absolute) ?? root;
            PushComponents(pending, absolute, resolved);
        }

        return Path.GetFullPath(resolved);
    }

    private static void PushComponents(Stack<string> pending, string path, string root)
    {
        var remainder = path.Substring(root.Length);
        var components = remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = components.Length - 1; i >= 0; i--)
        {
            pending.Push(components[i]);
        }
    }
}
