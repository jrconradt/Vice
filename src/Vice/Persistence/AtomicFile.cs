using System.Text;
using Vice.Logging;

namespace Vice.Persistence;

public static class AtomicFile
{
    private const int LOCK_ACQUIRE_TIMEOUT_MS = 10_000;
    private const int LOCK_RETRY_INITIAL_DELAY_MS = 1;
    private const int LOCK_RETRY_MAX_DELAY_MS = 25;
    private const long MAX_READ_BYTES = 256L * 1024 * 1024;

    public static Task WriteAllTextAsync(string path, string payload, CancellationToken ct)
        => WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(payload), ct);

    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        EnsureDirectory(path);

        await using var pathLock = await AcquireFileLockAsync(path, ct).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(path)!;
        SweepStaleTemp(dir, Path.GetFileName(path));
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fs = new FileStream(tmp, CreateWriteOptions()))
            {
                await fs.WriteAsync(payload, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                RandomAccess.FlushToDisk(fs.SafeFileHandle);
            }

            File.Move(tmp, path, overwrite: true);
            FileAccessControl.RestrictToCurrentUser(path);
            SafeFile.FlushDirectory(dir);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                Quietly.Swallow(cleanupEx);
            }
            throw;
        }
    }

    public static async Task<string?> ReadAllTextOrNullAsync(
        string path,
        CancellationToken ct,
        IViceLogger? logger = null)
    {
        var bytes = await ReadAllBytesOrNullAsync(path, ct, logger).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public static async Task<byte[]?> ReadAllBytesOrNullAsync(
        string path,
        CancellationToken ct,
        IViceLogger? logger = null)
    {
        var sink = logger ?? NullViceLogger.Instance;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > MAX_READ_BYTES)
            {
                sink.Log(ViceLogLevel.Warn,
                         $"AtomicFile read rejected: {path} is {fs.Length} bytes, exceeds cap {MAX_READ_BYTES}");
                return null;
            }

            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Log(ViceLogLevel.Warn, $"AtomicFile read denied: {path}", ex);
            return null;
        }
        catch (IOException ex)
        {
            sink.Log(ViceLogLevel.Warn, $"AtomicFile read failed: {path}", ex);
            return null;
        }
    }

    public static async Task AppendTextAsync(string path, string line, CancellationToken ct)
    {
        EnsureDirectory(path);

        await using var pathLock = await AcquireFileLockAsync(path, ct).ConfigureAwait(false);

        await using (var fs = new FileStream(path, CreateAppendOptions()))
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
            RandomAccess.FlushToDisk(fs.SafeFileHandle);
        }

        FileAccessControl.RestrictToCurrentUser(path);
        SafeFile.FlushDirectory(Path.GetDirectoryName(path)!);
    }

    private static void SweepStaleTemp(string dir, string fileName)
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(dir, $"{fileName}.*.tmp"))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Quietly.Swallow(ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Quietly.Swallow(ex);
        }
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || Directory.Exists(dir))
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static FileStreamOptions CreateWriteOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static FileStreamOptions CreateAppendOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static FileStreamOptions CreateLockOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 1,
            Options = FileOptions.DeleteOnClose,
        };

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static async Task<PathLock> AcquireFileLockAsync(string path, CancellationToken ct)
    {
        var lockPath = path + ".lock";
        EnsureDirectory(lockPath);

        var deadline = Environment.TickCount64 + LOCK_ACQUIRE_TIMEOUT_MS;
        var delay = LOCK_RETRY_INITIAL_DELAY_MS;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fs = new FileStream(lockPath, CreateLockOptions());
                return new PathLock(fs);
            }
            catch (IOException)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    throw new TimeoutException(
                        $"AtomicFile: could not acquire lock on {lockPath} within {LOCK_ACQUIRE_TIMEOUT_MS}ms; another writer holds it.");
                }

                var jitter = Random.Shared.Next(0, delay + 1);
                await Task.Delay(delay + jitter, ct).ConfigureAwait(false);

                delay = Math.Min(delay * 2, LOCK_RETRY_MAX_DELAY_MS);
            }
        }
    }

    private sealed class PathLock : IAsyncDisposable
    {
        private readonly FileStream _fs;
        private bool _disposed;

        public PathLock(FileStream fs)
        {
            _fs = fs;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                await _fs.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Quietly.Swallow(ex);
            }
        }
    }
}
