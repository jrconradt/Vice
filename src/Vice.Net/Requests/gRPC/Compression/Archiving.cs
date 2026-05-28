using System.Formats.Tar;
using System.IO.Compression;
using Vice.Logging;

namespace Vice.Network.gRPC.Compression;

public static class Archiving
{
    public const int MaxEntries = 10_000;

    public const long MaxTotalExpandedBytes = 1L << 30;

    public const long MaxPerEntryBytes = 256L << 20;

    private const uint UnixSymlinkMode = 0xA000;

    private static bool IsZipEntrySymlink(System.IO.Compression.ZipArchiveEntry entry)
    {
        var attr = (uint)entry.ExternalAttributes;
        var unixMode = (attr >> 16) & 0xF000;
        return unixMode == UnixSymlinkMode;
    }

    public static bool IsArchive(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.EndsWith(".zip", StringComparison.Ordinal)
            || name.EndsWith(".tar", StringComparison.Ordinal)
            || name.EndsWith(".tar.gz", StringComparison.Ordinal)
            || name.EndsWith(".tgz", StringComparison.Ordinal);
    }

    public static Task<string> ExtractAsync(string srcPath, string? destDir, CancellationToken ct)
        => ExtractAsync(srcPath, destDir, logger: null, ct);

    public static async Task<string> ExtractAsync(
        string srcPath,
        string? destDir,
        IViceLogger? logger,
        CancellationToken ct)
    {
        var log = logger ?? NullViceLogger.Instance;

        if (!File.Exists(srcPath))
        {
            throw new FileNotFoundException($"Archive not found: '{srcPath}'", srcPath);
        }

        var name = Path.GetFileName(srcPath).ToLowerInvariant();
        var ownsDest = destDir is null;
        var preExisted = !ownsDest && Directory.Exists(Path.GetFullPath(destDir!));
        var resolvedDest = ownsDest
            ? Path.Combine(Path.GetTempPath(), $"vice-unarchive-{Path.GetFileNameWithoutExtension(srcPath)}-{Guid.NewGuid():N}")
            : Path.GetFullPath(destDir!);

        Directory.CreateDirectory(resolvedDest);

        var destFullPath = Path.GetFullPath(resolvedDest)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destPrefix = destFullPath + Path.DirectorySeparatorChar;

        try
        {
            if (name.EndsWith(".zip", StringComparison.Ordinal))
            {
                await ExtractZipAsync(srcPath, destFullPath, destPrefix, log, ct).ConfigureAwait(false);
            }
            else if (name.EndsWith(".tar.gz", StringComparison.Ordinal) || name.EndsWith(".tgz", StringComparison.Ordinal))
            {
                await using var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await using var capped = Decompression.WithDecompressionCap(gz, MaxTotalExpandedBytes + (64L << 20));
                await ExtractTarAsync(capped, destFullPath, destPrefix, log, ct).ConfigureAwait(false);
            }
            else if (name.EndsWith(".tar", StringComparison.Ordinal))
            {
                await using var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await ExtractTarAsync(fs, destFullPath, destPrefix, log, ct).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Unsupported archive format: '{name}'. Supported: .zip, .tar, .tar.gz, .tgz");
            }
        }
        catch
        {
            if (ownsDest && !preExisted)
            {
                try
                {
                    Directory.Delete(resolvedDest, recursive: true);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    log.Log(ViceLogLevel.Debug, $"archive extract cleanup failed: '{resolvedDest}'", cleanupEx);
                }
            }
            throw;
        }

        return resolvedDest;
    }

    private static async Task ExtractZipAsync(
        string srcPath,
        string destFullPath,
        string destPrefix,
        IViceLogger log,
        CancellationToken ct)
    {
        await using var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException(
                $"Archive '{srcPath}' has {archive.Entries.Count} entries, exceeding cap {MaxEntries}.");
        }

        long total = 0;
        var count = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            count++;

            if (count > MaxEntries)
            {
                throw new InvalidDataException(
                    $"Archive '{srcPath}' exceeded entry cap {MaxEntries}.");
            }

            if (IsZipEntrySymlink(entry))
            {
                throw new InvalidDataException(
                    $"zip entry '{entry.FullName}' is a symlink; refused.");
            }

            if (entry.Length > MaxPerEntryBytes)
            {
                throw new InvalidDataException(
                    $"zip entry '{entry.FullName}' size {entry.Length} exceeds per-entry cap {MaxPerEntryBytes}.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destFullPath, entry.FullName));

            if (string.IsNullOrEmpty(entry.Name))
            {
                if (!targetPath.StartsWith(destPrefix, StringComparison.Ordinal)
                    && !string.Equals(targetPath, destFullPath, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"zip-slip: {entry.FullName}");
                }

                EnsureUnderRoot(targetPath, destFullPath, entry.FullName);
                Directory.CreateDirectory(targetPath);
                continue;
            }

            if (!targetPath.StartsWith(destPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"zip-slip: {entry.FullName}");
            }

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                EnsureUnderRoot(dir, destFullPath, entry.FullName);
                Directory.CreateDirectory(dir);
            }

            EnsureUnderRoot(targetPath, destFullPath, entry.FullName);

            await using var src = entry.Open();
            await using var dest = new FileStream(
                targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            total += await CopyCappedAsync(
                src, dest, MaxPerEntryBytes, MaxTotalExpandedBytes - total, entry.FullName, ct)
                .ConfigureAwait(false);
        }

        log.Log(ViceLogLevel.Debug,
            $"zip extracted: {count} entries, {total} bytes -> '{destFullPath}'");
    }

    private static async Task ExtractTarAsync(
        Stream tarStream,
        string destFullPath,
        string destPrefix,
        IViceLogger log,
        CancellationToken ct)
    {
        await using var reader = new TarReader(tarStream, leaveOpen: false);

        long total = 0;
        var count = 0;

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            count++;

            if (count > MaxEntries)
            {
                throw new InvalidDataException(
                    $"Tar archive exceeded entry cap {MaxEntries}.");
            }

            var entrySize = entry.Length;
            if (entrySize > MaxPerEntryBytes)
            {
                throw new InvalidDataException(
                    $"tar entry '{entry.Name}' size {entrySize} exceeds per-entry cap {MaxPerEntryBytes}.");
            }

            var entryName = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(destFullPath, entryName));

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                case TarEntryType.DirectoryList:
                    if (!targetPath.StartsWith(destPrefix, StringComparison.Ordinal)
                        && !string.Equals(targetPath, destFullPath, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"zip-slip: {entry.Name}");
                    }

                    EnsureUnderRoot(targetPath, destFullPath, entry.Name);
                    Directory.CreateDirectory(targetPath);
                    continue;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    if (!targetPath.StartsWith(destPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"zip-slip: {entry.Name}");
                    }

                    var dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        EnsureUnderRoot(dir, destFullPath, entry.Name);
                        Directory.CreateDirectory(dir);
                    }

                    EnsureUnderRoot(targetPath, destFullPath, entry.Name);

                    if (entry.DataStream is not null)
                    {
                        await using var dest = new FileStream(
                            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                            bufferSize: 81920, useAsync: true);
                        total += await CopyCappedAsync(
                            entry.DataStream, dest, MaxPerEntryBytes, MaxTotalExpandedBytes - total, entry.Name, ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await using var dest = new FileStream(
                            targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    }
                    continue;

                default:
                    log.Log(ViceLogLevel.Debug, $"tar entry '{entry.Name}' of type {entry.EntryType} skipped.");
                    continue;
            }
        }

        log.Log(ViceLogLevel.Debug,
            $"tar extracted: {count} entries, {total} bytes -> '{destFullPath}'");
    }

    private static void EnsureUnderRoot(string targetPath, string destFullPath, string entryName)
    {
        var rootCanonical = CanonicalizePath(destFullPath);
        var rootWithSep = rootCanonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var current = targetPath;
        var segments = new Stack<string>();

        while (!string.IsNullOrEmpty(current))
        {
            if (string.Equals(current, destFullPath, StringComparison.Ordinal))
            {
                return;
            }

            if (Directory.Exists(current) || File.Exists(current))
            {
                var canonical = CanonicalizePath(current);

                if (!string.Equals(canonical, rootCanonical, StringComparison.Ordinal)
                    && !canonical.StartsWith(rootWithSep, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"zip-slip: '{entryName}' resolves outside destination via symlink.");
                }

                while (segments.Count > 0)
                {
                    canonical = Path.Combine(canonical, segments.Pop());
                    var canonicalNorm = Path.GetFullPath(canonical);
                    if (!string.Equals(canonicalNorm, rootCanonical, StringComparison.Ordinal)
                        && !canonicalNorm.StartsWith(rootWithSep, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"zip-slip: '{entryName}' resolves outside destination via symlink.");
                    }
                }
                return;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            segments.Push(Path.GetFileName(current));
            current = parent;
        }
    }

    private static string CanonicalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        var resolved = full;
        var guard = 0;

        while (guard++ < 40)
        {
            FileSystemInfo? link;
            try
            {
                link = File.ResolveLinkTarget(resolved, returnFinalTarget: true);
            }
            catch (IOException)
            {
                link = null;
            }

            if (link is null)
            {
                break;
            }

            resolved = Path.GetFullPath(link.FullName);
        }

        return resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static async Task<long> CopyCappedAsync(
        Stream source,
        Stream destination,
        long entryCap,
        long globalRemaining,
        string entryName,
        CancellationToken ct)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > entryCap)
            {
                throw new InvalidDataException(
                    $"entry '{entryName}' decompressed size exceeded per-entry cap {entryCap}.");
            }

            if (written > globalRemaining)
            {
                throw new InvalidDataException(
                    $"entry '{entryName}' triggered archive total expanded cap (remaining budget {globalRemaining}).");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return written;
    }
}
