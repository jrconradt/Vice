using Vice.Composition;
using Vice.Configuration;
using Vice.Execution;
using Vice.Lexicon;
using static Vice.Dsl;

namespace Vice.Net.Commands.Cache;

[ViceCommandPack]
public static class CacheCommands
{
    private const string ResearchSubdir = "research";

    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Cache() > Nouns.Info(),
            "Report per-source research cache entry counts and total size",
            (ctx, ct) =>
            {
                var root = ResearchRoot();
                if (!Directory.Exists(root))
                {
                    EmitEmptyInfo(ctx, root);
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                var sources = CollectSources(root);
                EmitInfo(ctx, root, sources);
                return Task.FromResult(ViceExitCode.SUCCESS);
            });

        app.Register(
            Verbs.Cache() > noun("clear"),
            "Remove the entire research cache (or preview with --dry-run)",
            (ctx, ct) =>
            {
                var root = ResearchRoot();
                if (!Directory.Exists(root))
                {
                    Vice.Output.Line($"Cache is empty: {root}");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                var stats = Measure(root);
                if (ctx.DryRun)
                {
                    Vice.Output.Line($"[dry-run] Would remove {stats.Files} entries ({FormatSize(stats.Bytes)}) under {root}");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                Directory.Delete(root, recursive: true);
                Vice.Output.Line($"Removed {stats.Files} entries ({FormatSize(stats.Bytes)}) under {root}");
                return Task.FromResult(ViceExitCode.SUCCESS);
            });

        app.Register(
            Verbs.Cache() > noun("clear") > Nouns.Source() * Targets.Source,
            "Remove a single source's research cache subtree (or preview with --dry-run)",
            (ctx, ct) =>
            {
                var source = ctx["source"];
                if (string.IsNullOrWhiteSpace(source))
                {
                    Vice.Output.Error("cache clear source: a source name is required.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                var root = ResearchRoot();
                var rootFull = Path.GetFullPath(root);
                var sourceDir = Path.GetFullPath(Path.Combine(rootFull, source));
                var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
                    ? rootFull
                    : rootFull + Path.DirectorySeparatorChar;
                if (!sourceDir.StartsWith(prefix, StringComparison.Ordinal))
                {
                    Vice.Output.Error($"cache clear source: '{source}' escapes the research cache root.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                if (!Directory.Exists(sourceDir))
                {
                    Vice.Output.Line($"No cache for source '{source}': {sourceDir}");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                var stats = Measure(sourceDir);
                if (ctx.DryRun)
                {
                    Vice.Output.Line($"[dry-run] Would remove {stats.Files} entries ({FormatSize(stats.Bytes)}) for source '{source}' under {sourceDir}");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                Directory.Delete(sourceDir, recursive: true);
                Vice.Output.Line($"Removed {stats.Files} entries ({FormatSize(stats.Bytes)}) for source '{source}' under {sourceDir}");
                return Task.FromResult(ViceExitCode.SUCCESS);
            });

        app.Register(
            Verbs.Cache() > Nouns.List() > Nouns.Source() * Targets.Source,
            "List individual cached content IDs for one source",
            (ctx, ct) =>
            {
                var source = ctx["source"];
                if (string.IsNullOrWhiteSpace(source))
                {
                    Vice.Output.Error("cache list source: a source name is required.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                if (!TryResolveSourceDir(source, out var sourceDir))
                {
                    Vice.Output.Error($"cache list source: '{source}' escapes the research cache root.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                var items = CollectItems(sourceDir);
                EmitItems(ctx, source, items);
                return Task.FromResult(ViceExitCode.SUCCESS);
            });

        app.Register(
            Verbs.Cache() > noun("clear") > Nouns.Source() * Targets.Source > noun("id") * Targets.Id,
            "Invalidate a single cached content item by ID (or preview with --dry-run)",
            (ctx, ct) =>
            {
                var source = ctx["source"];
                var id = ctx["id"];
                if (string.IsNullOrWhiteSpace(source))
                {
                    Vice.Output.Error("cache clear source id: a source name is required.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                if (string.IsNullOrWhiteSpace(id))
                {
                    Vice.Output.Error("cache clear source id: an item id is required.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                if (!TryResolveSourceDir(source, out var sourceDir))
                {
                    Vice.Output.Error($"cache clear source id: '{source}' escapes the research cache root.");
                    return Task.FromResult(ViceExitCode.USAGE_ERROR);
                }

                var contentDir = Path.Combine(sourceDir, "content");
                var matches = MatchItems(contentDir, id);
                if (matches.Count == 0)
                {
                    Vice.Output.Line($"No cached item '{id}' for source '{source}'.");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                var bytes = 0L;
                foreach (var match in matches)
                {
                    bytes += SafeLength(match);
                }

                if (ctx.DryRun)
                {
                    Vice.Output.Line($"[dry-run] Would remove {matches.Count} file(s) ({FormatSize(bytes)}) for item '{id}' of source '{source}'");
                    return Task.FromResult(ViceExitCode.SUCCESS);
                }

                var removed = 0;
                foreach (var match in matches)
                {
                    if (TryDeleteFile(match))
                    {
                        removed++;
                    }
                }

                Vice.Output.Line($"Removed {removed} file(s) ({FormatSize(bytes)}) for item '{id}' of source '{source}'");
                return Task.FromResult(ViceExitCode.SUCCESS);
            });
    }

    private static bool TryResolveSourceDir(string source, out string sourceDir)
    {
        var rootFull = Path.GetFullPath(ResearchRoot());
        sourceDir = Path.GetFullPath(Path.Combine(rootFull, source));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        return sourceDir.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static IReadOnlyList<CachedItem> CollectItems(string sourceDir)
    {
        var result = new List<CachedItem>();
        foreach (var leaf in new[] { "content", "search" })
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
                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    pending.Push(sub);
                }

                foreach (var file in Directory.EnumerateFiles(current))
                {
                    if (file.EndsWith(".lock", StringComparison.Ordinal)
                        || file.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(new CachedItem(
                        leaf,
                        Path.GetFileName(file),
                        SafeLength(file)));
                }
            }
        }

        result.Sort((a, b) =>
        {
            var byLeaf = string.CompareOrdinal(a.Leaf, b.Leaf);
            return byLeaf != 0 ? byLeaf : string.CompareOrdinal(a.Id, b.Id);
        });
        return result;
    }

    private static IReadOnlyList<string> MatchItems(string contentDir, string id)
    {
        var result = new List<string>();
        if (!Directory.Exists(contentDir))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(contentDir))
        {
            if (file.EndsWith(".lock", StringComparison.Ordinal)
                || file.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            var name = Path.GetFileName(file);
            var stem = name;
            var dot = name.IndexOf('.');
            if (dot >= 0)
            {
                stem = name[..dot];
            }

            if (string.Equals(name, id, StringComparison.Ordinal)
                || string.Equals(stem, id, StringComparison.Ordinal))
            {
                result.Add(file);
            }
        }

        return result;
    }

    private static bool TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EmitItems(
        CommandContext ctx,
        string source,
        IReadOnlyList<CachedItem> items)
    {
        if (ctx.WantsJson)
        {
            var entries = items.Select(item =>
                $"{{\"kind\":{JsonString(item.Leaf)}," +
                $"\"id\":{JsonString(item.Id)},\"bytes\":{item.Bytes}}}");
            var itemsJson = string.Join(",", entries);
            Vice.Output.Line(
                $"{{\"source\":{JsonString(source)},\"count\":{items.Count},\"items\":[{itemsJson}]}}");
            return;
        }

        if (items.Count == 0)
        {
            Vice.Output.Line($"No cached items for source '{source}'.");
            return;
        }

        Vice.Output.Line($"  {"KIND",-8} {"ID",-40} {"SIZE",12}");
        foreach (var item in items)
        {
            Vice.Output.Line($"  {item.Leaf,-8} {item.Id,-40} {FormatSize(item.Bytes),12}");
        }
    }

    private static string ResearchRoot()
    {
        var dirs = new ViceDirectories("vice");
        return Path.Combine(dirs.CacheDir, ResearchSubdir);
    }

    private static IReadOnlyList<SourceUsage> CollectSources(string root)
    {
        var result = new List<SourceUsage>();
        foreach (var sourceDir in Directory.EnumerateDirectories(root))
        {
            var search = Measure(Path.Combine(sourceDir, "search"));
            var content = Measure(Path.Combine(sourceDir, "content"));
            var other = MeasureExcluding(
                sourceDir,
                "search",
                "content");

            var name = Path.GetFileName(sourceDir);
            result.Add(new SourceUsage(
                name,
                search,
                content,
                other));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static Usage Measure(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return new Usage(0, 0);
        }

        var files = 0L;
        var bytes = 0L;
        var pending = new Stack<string>();
        pending.Push(dir);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(current))
            {
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                files++;
                bytes += SafeLength(file);
            }
        }

        return new Usage(files, bytes);
    }

    private static Usage MeasureExcluding(
        string dir,
        string skipA,
        string skipB)
    {
        if (!Directory.Exists(dir))
        {
            return new Usage(0, 0);
        }

        var files = 0L;
        var bytes = 0L;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            files++;
            bytes += SafeLength(file);
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (string.Equals(name, skipA, StringComparison.Ordinal)
                || string.Equals(name, skipB, StringComparison.Ordinal))
            {
                continue;
            }

            var nested = Measure(sub);
            files += nested.Files;
            bytes += nested.Bytes;
        }

        return new Usage(files, bytes);
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void EmitEmptyInfo(CommandContext ctx, string root)
    {
        if (ctx.WantsJson)
        {
            Vice.Output.Line($"{{\"root\":{JsonString(root)},\"totalFiles\":0,\"totalBytes\":0,\"sources\":[]}}");
            return;
        }

        Vice.Output.Line($"Research cache root: {root}");
        Vice.Output.Line("Cache is empty.");
    }

    private static void EmitInfo(
        CommandContext ctx,
        string root,
        IReadOnlyList<SourceUsage> sources)
    {
        var totalFiles = 0L;
        var totalBytes = 0L;
        foreach (var source in sources)
        {
            totalFiles += source.Total.Files;
            totalBytes += source.Total.Bytes;
        }

        if (ctx.WantsJson)
        {
            EmitInfoJson(
                root,
                sources,
                totalFiles,
                totalBytes);
            return;
        }

        Vice.Output.Line($"Research cache root: {root}");
        if (sources.Count == 0)
        {
            Vice.Output.Line("Cache is empty.");
            return;
        }

        Vice.Output.Line($"  {"SOURCE",-20} {"SEARCH",10} {"CONTENT",10} {"TOTAL",10} {"SIZE",12}");
        foreach (var source in sources)
        {
            Vice.Output.Line($"  {source.Name,-20} {source.Search.Files,10} {source.Content.Files,10} {source.Total.Files,10} {FormatSize(source.Total.Bytes),12}");
        }

        Vice.Output.Line($"  {"TOTAL",-20} {"",10} {"",10} {totalFiles,10} {FormatSize(totalBytes),12}");
    }

    private static void EmitInfoJson(
        string root,
        IReadOnlyList<SourceUsage> sources,
        long totalFiles,
        long totalBytes)
    {
        var entries = sources.Select(source =>
            $"{{\"source\":{JsonString(source.Name)}," +
            $"\"searchFiles\":{source.Search.Files},\"searchBytes\":{source.Search.Bytes}," +
            $"\"contentFiles\":{source.Content.Files},\"contentBytes\":{source.Content.Bytes}," +
            $"\"totalFiles\":{source.Total.Files},\"totalBytes\":{source.Total.Bytes}}}");
        var sourcesJson = string.Join(",", entries);
        Vice.Output.Line(
            $"{{\"root\":{JsonString(root)},\"totalFiles\":{totalFiles},\"totalBytes\":{totalBytes},\"sources\":[{sourcesJson}]}}");
    }

    private static string JsonString(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024
            && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.0} {units[unit]}";
    }

    private readonly record struct Usage(long Files, long Bytes);

    private readonly record struct CachedItem(
        string Leaf,
        string Id,
        long Bytes);

    private sealed record SourceUsage(
        string Name,
        Usage Search,
        Usage Content,
        Usage Other)
    {
        public Usage Total => new(
            Search.Files + Content.Files + Other.Files,
            Search.Bytes + Content.Bytes + Other.Bytes);
    }
}
