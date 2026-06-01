using Microsoft.Win32.SafeHandles;

namespace Vice.Persistence;

public static class SafeFile
{
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Quietly.Swallow(ex);
        }
    }

    public static void FlushToDisk(SafeFileHandle handle)
    {
        RandomAccess.FlushToDisk(handle);
    }

    public static void FlushDirectory(string dir)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        try
        {
            using var handle = File.OpenHandle(dir, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            RandomAccess.FlushToDisk(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Quietly.Swallow(ex);
        }
    }
}
