using Vice.Logging;
using Vice.Net.Http;
using Vice.Persistence;

namespace Vice.Net.Research;

internal static class ResearchDownloader
{
    public static async Task<long> DownloadToFileAsync(HttpClient http,
                                                       Uri uri,
                                                       string destinationPath,
                                                       IProgress<DownloadProgress>? progress,
                                                       CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (!SafeWriteRoots.IsAllowed(fullPath, out var reason))
        {
            throw new BadArgument($"Destination '{fullPath}' is outside allowed write roots: {reason}.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partial = $"{fullPath}.{Guid.NewGuid():N}.partial";
        try
        {
            long written;
            var observed = new ExpectedLengthObserver(progress);
            await using (var file = new FileStream(partial,
                                                   FileMode.CreateNew,
                                                   FileAccess.ReadWrite,
                                                   FileShare.None))
            {
                var resumable = new ResumableHttpStream(http, uri);
                file.Seek(0, SeekOrigin.Begin);
                file.SetLength(0);

                await resumable.DownloadAsync(file, startOffset: 0, observed, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                written = file.Length;
            }

            if (observed.ExpectedTotal is long expected
                && written != expected)
            {
                throw new InvalidDataException(
                    $"Download of '{uri}' is incomplete: wrote {written} bytes but the server advertised {expected}; refusing to promote a truncated file.");
            }

            File.Move(partial, fullPath, overwrite: true);
            return written;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public static string BuildUrlDestinationPath(string? toPath,
                                                 string fileName)
    {
        var safeName = Sanitize(fileName);
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, safeName);
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, safeName);
        }

        return full;
    }

    public static string BuildDestinationPath(string? toPath,
                                              string source,
                                              string id,
                                              string extension)
    {
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, $"{Sanitize(id)}.{extension}");
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, $"{Sanitize(id)}.{extension}");
        }

        return full;
    }

    private static bool EndsWithSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar);
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ExpectedLengthObserver : IProgress<DownloadProgress>
    {
        private readonly IProgress<DownloadProgress>? _inner;
        private long _expected = -1;

        public ExpectedLengthObserver(IProgress<DownloadProgress>? inner)
        {
            _inner = inner;
        }

        public long? ExpectedTotal => _expected >= 0 ? _expected : null;

        public void Report(DownloadProgress value)
        {
            if (value.TotalBytes is long total
                && total >= 0)
            {
                Volatile.Write(ref _expected, total);
            }

            _inner?.Report(value);
        }
    }
}
