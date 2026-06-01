using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Vice.Composition;
using Vice.Contracts;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Streaming;
using static Vice.Dsl;

namespace Vice.Files;

[ViceCommandPack]
public static class FileSearchCommands
{
    private const int MaxPredicates = 3;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly IReadOnlyDictionary<string, string[]> TypeAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = new[] { ".cs" },
            ["c#"] = new[] { ".cs" },
            ["fsharp"] = new[] { ".fs", ".fsi", ".fsx" },
            ["f#"] = new[] { ".fs", ".fsi", ".fsx" },
            ["typescript"] = new[] { ".ts", ".tsx", ".mts", ".cts" },
            ["javascript"] = new[] { ".js", ".jsx", ".mjs", ".cjs" },
            ["python"] = new[] { ".py", ".pyi", ".pyw" },
            ["rust"] = new[] { ".rs" },
            ["go"] = new[] { ".go" },
            ["java"] = new[] { ".java" },
            ["markdown"] = new[] { ".md", ".markdown", ".mdown" },
            ["json"] = new[] { ".json", ".jsonl", ".ndjson" },
            ["yaml"] = new[] { ".yaml", ".yml" },
            ["xml"] = new[] { ".xml", ".xsd", ".xsl", ".xslt" },
            ["shell"] = new[] { ".sh", ".bash", ".zsh", ".ksh", ".fish" },
            ["pdf"] = new[] { ".pdf" },
            ["image"] = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg", ".ico" },
            ["archive"] = new[] { ".zip", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".7z", ".rar" },
        };

    public static void Register(IViceApp app)
    {
        var filesChain =
            Verbs.Search()
            > optional(Nouns.For())
            > Nouns.Files()
            > repeat(Connectors.By() * Targets.Axis * Targets.Pattern, min: 1, max: MaxPredicates, separator: Connectors.And())
            > optional(Connectors.In() * Targets.Root);

        var foldersChain =
            Verbs.Search()
            > optional(Nouns.For())
            > Nouns.Folders()
            > repeat(Connectors.By() * Targets.Axis * Targets.Pattern, min: 1, max: MaxPredicates, separator: Connectors.And())
            > optional(Connectors.In() * Targets.Root);

        app.RegisterStreaming<byte[]>(
            filesChain,
            "Search files by axis-keyed predicates; emits matched file contents when piped",
            StreamFilesAsync,
            classicFallback: PrintFilesAsync);

        app.Register(
            foldersChain,
            "Search folders by axis-keyed predicates; prints matched directory paths",
            PrintFoldersAsync);
    }

    private static Task<int> PrintFilesAsync(CommandContext ctx, CancellationToken ct)
    {
        var predicates = BuildPredicates(ctx);
        if (predicates is null)
        {
            Vice.Output.Error("search files: no valid predicate. Use 'by <axis> <pattern>'.");
            return Task.FromResult(ViceExitCode.USAGE_ERROR);
        }

        var root = ResolveRoot(ctx);
        var options = WalkOptions.FromContext(ctx);
        foreach (var path in Walk(root, WalkKind.Files, predicates, options, ct))
        {
            ct.ThrowIfCancellationRequested();
            Vice.Output.Line(path);
        }

        return Task.FromResult(ViceExitCode.SUCCESS);
    }

    private static Task<int> PrintFoldersAsync(CommandContext ctx, CancellationToken ct)
    {
        var predicates = BuildPredicates(ctx);
        if (predicates is null)
        {
            Vice.Output.Error("search folders: no valid predicate. Use 'by <axis> <pattern>'.");
            return Task.FromResult(ViceExitCode.USAGE_ERROR);
        }

        var root = ResolveRoot(ctx);
        var options = WalkOptions.FromContext(ctx);
        foreach (var path in Walk(root, WalkKind.Folders, predicates, options, ct))
        {
            ct.ThrowIfCancellationRequested();
            Vice.Output.Line(path);
        }

        return Task.FromResult(ViceExitCode.SUCCESS);
    }

    private static async Task<int> StreamFilesAsync(IStreamingCommandContext<byte[]> ctx, CancellationToken ct)
    {
        var predicates = BuildPredicates(ctx);
        if (predicates is null)
        {
            ctx.Stream.Complete();
            return ViceExitCode.USAGE_ERROR;
        }

        var root = ResolveRoot(ctx);
        var options = WalkOptions.FromContext(ctx);
        var chunkSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? BufferConstants.FILE_IO;

        foreach (var path in Walk(root, WalkKind.Files, predicates, options, ct))
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: chunkSize,
                useAsync: true);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var buffer = new byte[chunkSize];
                var read = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), ct);
                if (read == 0)
                {
                    break;
                }

                if (read == chunkSize)
                {
                    await ctx.Stream.YieldAsync(buffer, ct);
                }
                else
                {
                    var trimmed = new byte[read];
                    Array.Copy(buffer, trimmed, read);
                    await ctx.Stream.YieldAsync(trimmed, ct);
                }
            }
        }

        ctx.Stream.Complete();
        return ViceExitCode.SUCCESS;
    }

    private static List<Predicate>? BuildPredicates(ICommandContext ctx)
    {
        var axes = ctx.GetTargets("axis");
        var patterns = ctx.GetTargets("pattern");
        var count = Math.Min(axes.Count, patterns.Count);
        if (count == 0)
        {
            return null;
        }

        var useRegex = ctx.HasGlobalOption("regex");
        var predicates = new List<Predicate>(count);
        for (int i = 0; i < count; i++)
        {
            var predicate = Predicate.Parse(axes[i], patterns[i], useRegex);
            if (predicate is null)
            {
                return null;
            }

            predicates.Add(predicate);
        }

        return predicates;
    }

    private static string ResolveRoot(ICommandContext ctx)
    {
        var root = ctx.GetTarget("root");
        return Path.GetFullPath(string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root);
    }

    private enum WalkKind
    {
        Files,
        Folders,
    }

    private readonly record struct WalkOptions(bool IncludeHidden, bool FollowSymlinks, int MaxDepth)
    {
        public static WalkOptions FromContext(ICommandContext ctx)
        {
            var includeHidden = ctx.HasGlobalOption("include-hidden");
            var followSymlinks = ctx.HasGlobalOption("follow-symlinks");
            var depth = ctx.GetGlobalOption("depth").AsPositiveInt() ?? int.MaxValue;
            return new WalkOptions(includeHidden, followSymlinks, depth);
        }
    }

    private static IEnumerable<string> Walk(
        string root,
        WalkKind kind,
        IReadOnlyList<Predicate> predicates,
        WalkOptions options,
        CancellationToken ct)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();

            var entries = EnumerateEntries(dir);
            entries.Sort(StringComparer.Ordinal);

            var children = new List<(string Dir, int Depth)>();
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                FileSystemInfo info;
                try
                {
                    info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
                }
                catch (IOException)
                {
                    continue;
                }

                var isHidden = (info.Attributes & FileAttributes.Hidden) != 0
                    || info.Name.StartsWith('.');
                if (isHidden && !options.IncludeHidden)
                {
                    continue;
                }

                var isSymlink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
                var isDirectory = info is DirectoryInfo;

                if (isDirectory)
                {
                    var descend = depth < options.MaxDepth
                        && (!isSymlink || options.FollowSymlinks);
                    if (descend)
                    {
                        children.Add((entry, depth + 1));
                    }

                    if (kind == WalkKind.Folders && Matches(predicates, info))
                    {
                        yield return entry;
                    }
                }
                else if (kind == WalkKind.Files && Matches(predicates, info))
                {
                    yield return entry;
                }
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    private static List<string> EnumerateEntries(string dir)
    {
        var entries = new List<string>();
        IEnumerator<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
        }
        catch (UnauthorizedAccessException)
        {
            return entries;
        }
        catch (DirectoryNotFoundException)
        {
            return entries;
        }
        catch (IOException)
        {
            return entries;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                entries.Add(enumerator.Current);
            }
        }

        return entries;
    }

    private static bool Matches(IReadOnlyList<Predicate> predicates, FileSystemInfo info)
    {
        for (int i = 0; i < predicates.Count; i++)
        {
            if (!predicates[i].Evaluate(info))
            {
                return false;
            }
        }

        return true;
    }

    private abstract class Predicate
    {
        public abstract bool Evaluate(FileSystemInfo info);

        public static Predicate? Parse(string axis, string pattern, bool useRegex)
        {
            return axis.ToLowerInvariant() switch
            {
                "name" => NamePredicate.Create(pattern, useRegex, fullPath: false),
                "path" => NamePredicate.Create(pattern, useRegex, fullPath: true),
                "type" => new TypePredicate(pattern),
                "size" => SizePredicate.Parse(pattern),
                "mtime" => MtimePredicate.Parse(pattern),
                _ => null,
            };
        }
    }

    private sealed class NamePredicate : Predicate
    {
        private readonly string _pattern;
        private readonly Regex? _regex;
        private readonly bool _fullPath;
        private bool _timeoutWarned;

        private NamePredicate(string pattern, Regex? regex, bool fullPath)
        {
            _pattern = pattern;
            _regex = regex;
            _fullPath = fullPath;
        }

        public static NamePredicate? Create(string pattern, bool useRegex, bool fullPath)
        {
            if (!useRegex)
            {
                return new NamePredicate(pattern, null, fullPath);
            }

            try
            {
                var regex = new Regex(
                    pattern,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant,
                    RegexTimeout);
                return new NamePredicate(pattern, regex, fullPath);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public override bool Evaluate(FileSystemInfo info)
        {
            var subject = _fullPath ? info.FullName : info.Name;
            if (_regex is not null)
            {
                try
                {
                    return _regex.IsMatch(subject);
                }
                catch (RegexMatchTimeoutException)
                {
                    if (!_timeoutWarned)
                    {
                        _timeoutWarned = true;
                        Vice.Log.Emit(
                            ViceLogLevel.Warn,
                            $"search: regex pattern '{_pattern}' timed out evaluating an entry; results may be incomplete.");
                    }

                    return false;
                }
            }

            return FileSystemName.MatchesSimpleExpression(_pattern, subject, ignoreCase: true);
        }
    }

    private sealed class TypePredicate : Predicate
    {
        private readonly string[] _extensions;

        public TypePredicate(string pattern)
        {
            if (TypeAliases.TryGetValue(pattern, out var mapped))
            {
                _extensions = mapped;
            }
            else
            {
                var ext = pattern.StartsWith('.') ? pattern : $".{pattern}";
                _extensions = new[] { ext };
            }
        }

        public override bool Evaluate(FileSystemInfo info)
        {
            var ext = Path.GetExtension(info.Name);
            for (int i = 0; i < _extensions.Length; i++)
            {
                if (string.Equals(ext, _extensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private enum SizeComparison
    {
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        Equal,
    }

    private sealed class SizePredicate : Predicate
    {
        private readonly SizeComparison _comparison;
        private readonly long _bytes;

        private SizePredicate(SizeComparison comparison, long bytes)
        {
            _comparison = comparison;
            _bytes = bytes;
        }

        public static SizePredicate? Parse(string pattern)
        {
            var span = pattern.Trim();
            if (span.Length == 0)
            {
                return null;
            }

            var comparison = SizeComparison.Equal;
            var index = 0;
            if (span.StartsWith(">=", StringComparison.Ordinal))
            {
                comparison = SizeComparison.GreaterOrEqual;
                index = 2;
            }
            else if (span.StartsWith("<=", StringComparison.Ordinal))
            {
                comparison = SizeComparison.LessOrEqual;
                index = 2;
            }
            else if (span.StartsWith(">", StringComparison.Ordinal))
            {
                comparison = SizeComparison.Greater;
                index = 1;
            }
            else if (span.StartsWith("<", StringComparison.Ordinal))
            {
                comparison = SizeComparison.Less;
                index = 1;
            }
            else if (span.StartsWith("=", StringComparison.Ordinal))
            {
                comparison = SizeComparison.Equal;
                index = 1;
            }

            var magnitude = span[index..].Trim();
            var bytes = ParseBytes(magnitude);
            if (bytes is null)
            {
                return null;
            }

            return new SizePredicate(comparison, bytes.Value);
        }

        public override bool Evaluate(FileSystemInfo info)
        {
            if (info is not FileInfo file)
            {
                return false;
            }

            long size;
            try
            {
                size = file.Length;
            }
            catch (IOException)
            {
                return false;
            }

            return _comparison switch
            {
                SizeComparison.Greater => size > _bytes,
                SizeComparison.GreaterOrEqual => size >= _bytes,
                SizeComparison.Less => size < _bytes,
                SizeComparison.LessOrEqual => size <= _bytes,
                _ => size == _bytes,
            };
        }

        private static long? ParseBytes(string magnitude)
        {
            if (magnitude.Length == 0)
            {
                return null;
            }

            var digitsEnd = 0;
            while (digitsEnd < magnitude.Length
                   && (char.IsDigit(magnitude[digitsEnd]) || magnitude[digitsEnd] == '.'))
            {
                digitsEnd++;
            }

            if (digitsEnd == 0)
            {
                return null;
            }

            var numberText = magnitude[..digitsEnd];
            var suffix = magnitude[digitsEnd..].Trim();
            if (!double.TryParse(numberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            var multiplier = SuffixMultiplier(suffix);
            if (multiplier is null)
            {
                return null;
            }

            var bytes = value * multiplier.Value;
            if (double.IsNaN(bytes)
                || bytes < 0d
                || bytes >= 9223372036854775808d)
            {
                return null;
            }

            return (long)bytes;
        }

        private static double? SuffixMultiplier(string suffix)
        {
            return suffix.ToUpperInvariant() switch
            {
                "" or "B" => 1d,
                "K" => 1_000d,
                "M" => 1_000_000d,
                "G" => 1_000_000_000d,
                "T" => 1_000_000_000_000d,
                "KI" => 1024d,
                "MI" => 1024d * 1024d,
                "GI" => 1024d * 1024d * 1024d,
                "TI" => 1024d * 1024d * 1024d * 1024d,
                _ => null,
            };
        }
    }

    private enum MtimeComparison
    {
        Since,
        Before,
    }

    private sealed class MtimePredicate : Predicate
    {
        private readonly MtimeComparison _comparison;
        private readonly DateTime _thresholdUtc;

        private MtimePredicate(MtimeComparison comparison, DateTime thresholdUtc)
        {
            _comparison = comparison;
            _thresholdUtc = thresholdUtc;
        }

        public static MtimePredicate? Parse(string pattern)
        {
            var trimmed = pattern.Trim();
            MtimeComparison comparison;
            string remainder;
            if (trimmed.StartsWith("since ", StringComparison.OrdinalIgnoreCase))
            {
                comparison = MtimeComparison.Since;
                remainder = trimmed[6..].Trim();
            }
            else if (trimmed.StartsWith("before ", StringComparison.OrdinalIgnoreCase))
            {
                comparison = MtimeComparison.Before;
                remainder = trimmed[7..].Trim();
            }
            else
            {
                return null;
            }

            var threshold = ParseWhen(remainder);
            if (threshold is null)
            {
                return null;
            }

            return new MtimePredicate(comparison, threshold.Value);
        }

        public override bool Evaluate(FileSystemInfo info)
        {
            var modified = info.LastWriteTimeUtc;
            return _comparison switch
            {
                MtimeComparison.Since => modified >= _thresholdUtc,
                _ => modified < _thresholdUtc,
            };
        }

        private static DateTime? ParseWhen(string when)
        {
            if (when.Length == 0)
            {
                return null;
            }

            var duration = ParseDuration(when);
            if (duration is not null)
            {
                return DateTime.UtcNow - duration.Value;
            }

            if (DateTime.TryParse(when, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var iso))
            {
                return iso;
            }

            return null;
        }

        private static TimeSpan? ParseDuration(string text)
        {
            if (text.Length < 2)
            {
                return null;
            }

            var unit = text[^1];
            var numberText = text[..^1];
            if (!long.TryParse(numberText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            return char.ToLowerInvariant(unit) switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                'w' => TimeSpan.FromDays(amount * 7),
                _ => null,
            };
        }
    }
}
