using System.Security.Cryptography;
using Vice.Logging;
using Vice.Net.Requests.Http;
using Vice.Persistence;

namespace Vice.Research;

internal static class AtomicDownload
{
    public static async Task<long> RunAsync(ResumableHttpStream resumable,
                                            Uri uri,
                                            string fullPath,
                                            string partial,
                                            FileMode mode,
                                            long startOffset,
                                            IProgress<DownloadProgress>? progress,
                                            IViceLogger logger,
                                            CancellationToken ct)
    {
        try
        {
            long written;
            var observed = new ExpectedLengthObserver(progress);
            await using (var file = new FileStream(partial,
                                                   mode,
                                                   FileAccess.ReadWrite,
                                                   FileShare.None))
            {
                file.SetLength(startOffset);
                file.Seek(startOffset, SeekOrigin.Begin);

                await resumable.DownloadAsync(file, startOffset, observed, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                SafeFile.FlushToDisk(file.SafeFileHandle);
                written = file.Length;
            }

            if (observed.ExpectedTotal is not long expected)
            {
                throw new InvalidDataException(
                    $"Download of '{uri}' cannot be verified: the server advertised no Content-Length or Content-Range, so completion is indeterminate; refusing to promote an unverifiable file.");
            }

            if (written != expected)
            {
                throw new InvalidDataException(
                    $"Download of '{uri}' is incomplete: wrote {written} bytes but the server advertised {expected}; refusing to promote a truncated file.");
            }

            var digest = await ComputeSha256Async(partial, ct).ConfigureAwait(false);

            File.Move(partial, fullPath, overwrite: true);
            var promotedDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(promotedDir))
            {
                SafeFile.FlushDirectory(promotedDir);
            }

            logger.Log(ViceLogLevel.Info,
                       $"research download integrity sha256={digest} bytes={written} for '{uri}' -> {fullPath}");

            return written;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SafeFile.TryDelete(partial);
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path,
                                                         CancellationToken ct)
    {
        await using var stream = new FileStream(path,
                                                FileMode.Open,
                                                FileAccess.Read,
                                                FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static string ResolveDestination(string destinationPath)
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

        return fullPath;
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
