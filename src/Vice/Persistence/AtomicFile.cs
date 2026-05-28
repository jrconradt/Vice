using System.Text;
using Vice.Logging;

namespace Vice.Persistence;

public static class AtomicFile
{
    private const int LockAcquireTimeoutMs = 10_000;
    private const int LockRetryDelayMs = 25;

    public static Task WriteAllTextAsync(string path, string payload, CancellationToken ct)
        => WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(payload), ct);

    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        EnsureDirectory(path);

        await using var pathLock = await AcquireLockAsync(path, ct).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(path)!;
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fs = new FileStream(tmp, CreateWriteOptions()))
            {
                await fs.WriteAsync(payload, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                try
                {
                    fs.SafeFileHandle.Close();
                }
                catch (ObjectDisposedException ode)
                {
                    System.Diagnostics.Debug.WriteLine(ode);
                }
            }

            File.Move(tmp, path, overwrite: true);
            FileAccessControl.RestrictToCurrentUser(path);
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
                System.Diagnostics.Debug.WriteLine(cleanupEx);
            }
            throw;
        }
    }

    public static async Task<string?> ReadAllTextOrNullAsync(string path, CancellationToken ct)
    {
        var bytes = await ReadAllBytesOrNullAsync(path, ct).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public static async Task<byte[]?> ReadAllBytesOrNullAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var pathLock = await AcquireLockAsync(path, ct).ConfigureAwait(false);

            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
            Vice.Log.Emit(ViceLogLevel.Warn, $"AtomicFile read denied: {path}", ex); return null;
        }
        catch (IOException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"AtomicFile read failed: {path}", ex); return null;
        }
    }

    public static async Task AppendTextAsync(string path, string line, CancellationToken ct)
    {
        EnsureDirectory(path);

        await using var pathLock = await AcquireLockAsync(path, ct).ConfigureAwait(false);

        await using var fs = new FileStream(path, CreateAppendOptions());
        var bytes = Encoding.UTF8.GetBytes(line);
        await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
        await fs.FlushAsync(ct).ConfigureAwait(false);
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || Directory.Exists(dir))
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
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

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
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

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
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

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static async Task<PathLock> AcquireLockAsync(string path, CancellationToken ct)
    {
        var lockPath = path + ".lock";
        EnsureDirectory(lockPath);

        var deadline = Environment.TickCount64 + LockAcquireTimeoutMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fs = new FileStream(lockPath, CreateLockOptions());
                return new PathLock(fs, lockPath);
            }
            catch (IOException)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    throw new TimeoutException(
                        $"AtomicFile: could not acquire lock on {lockPath} within {LockAcquireTimeoutMs}ms; another writer holds it.");
                }
                try
                {
                    await Task.Delay(LockRetryDelayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
    }

    private sealed class PathLock : IAsyncDisposable
    {
        private readonly FileStream _fs;
        private readonly string _lockPath;
        private int _disposed;

        public PathLock(FileStream fs, string lockPath)
        {
            _fs = fs;
            _lockPath = lockPath;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await _fs.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }
}
