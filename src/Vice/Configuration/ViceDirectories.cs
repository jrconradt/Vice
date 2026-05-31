namespace Vice.Configuration;

public sealed class ViceDirectories
{
    public string AppName { get; }
    public string ConfigDir { get; }
    public string DataDir { get; }
    public string CacheDir { get; }
    public string StateDir { get; }
    public string? RuntimeDir { get; }
    public string? LegacyDir { get; }

    public ViceDirectories(
        string appName,
        string? configHome = null,
        string? dataHome = null,
        string? cacheHome = null,
        string? stateHome = null,
        string? runtimeDir = null,
        bool useLegacyFallback = true)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("appName must be non-empty.", nameof(appName));
        }

        AppName = appName;

        ConfigDir = ResolveDir(configHome, "VICE_CONFIG_HOME", "XDG_CONFIG_HOME",
            DefaultConfigHome, appName);
        DataDir = ResolveDir(dataHome, "VICE_DATA_HOME", "XDG_DATA_HOME",
            DefaultDataHome, appName);
        CacheDir = ResolveDir(cacheHome, "VICE_CACHE_HOME", "XDG_CACHE_HOME",
            DefaultCacheHome, appName);
        StateDir = ResolveDir(stateHome, "VICE_STATE_HOME", "XDG_STATE_HOME",
            DefaultStateHome, appName);

        RuntimeDir = ResolveRuntimeDir(runtimeDir, appName);

        LegacyDir = useLegacyFallback ? FindLegacyDir(appName) : null;
    }

    public static ViceDirectories UnifiedAt(string appName, string singleDir)
        => new(
            appName,
            configHome: singleDir,
            dataHome: singleDir,
            cacheHome: singleDir,
            stateHome: singleDir,
            runtimeDir: singleDir,
            useLegacyFallback: false);

    private static string ResolveDir(
        string? overrideValue,
        string viceVar,
        string xdgVar,
        Func<string> platformDefault,
        string appName)
    {
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        var viceOverride = Environment.GetEnvironmentVariable(viceVar);
        if (!string.IsNullOrEmpty(viceOverride))
        {
            return ExpandHome(viceOverride);
        }

        var xdgOverride = Environment.GetEnvironmentVariable(xdgVar);
        if (!string.IsNullOrEmpty(xdgOverride))
        {
            return Path.Combine(ExpandHome(xdgOverride), appName);
        }

        return Path.Combine(platformDefault(), appName);
    }

    private static string? ResolveRuntimeDir(string? overrideValue, string appName)
    {
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        var viceOverride = Environment.GetEnvironmentVariable("VICE_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(viceOverride))
        {
            return ExpandHome(viceOverride);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, appName);
        }

        return null;
    }

    private static string? FindLegacyDir(string appName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return null;
        }

        var legacy = Path.Combine(home, "." + appName);
        return Directory.Exists(legacy) ? legacy : null;
    }

    private static string DefaultConfigHome() => ExpandHome("~/.config");

    private static string DefaultDataHome()
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return !string.IsNullOrEmpty(lad) ? lad : ExpandHome("~/.local/share");
    }

    private static string DefaultCacheHome() => ExpandHome("~/.cache");

    private static string DefaultStateHome() => ExpandHome("~/.local/state");

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                home = Path.GetTempPath();
            }

            return Path.Combine(home, path[2..]);
        }
        return path;
    }

    public string ResolveFileWithLegacy(string modernDir, string fileName)
    {
        var modern = Path.Combine(modernDir, fileName);
        if (File.Exists(modern))
        {
            return modern;
        }

        if (LegacyDir is not null)
        {
            var legacy = Path.Combine(LegacyDir, fileName);
            if (File.Exists(legacy))
            {
                if (TryCopyForward(legacy, modernDir, modern))
                {
                    return modern;
                }

                return legacy;
            }
        }
        return modern;
    }

    private static bool TryCopyForward(string legacy, string modernDir, string modern)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                || OperatingSystem.IsFreeBSD())
            {
                Directory.CreateDirectory(modernDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else
            {
                Directory.CreateDirectory(modernDir);
            }

            File.Copy(legacy, modern, overwrite: false);
            Vice.Persistence.FileAccessControl.RestrictToCurrentUser(modern);
            return File.Exists(modern);
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException
            || ex is NotSupportedException)
        {
            return File.Exists(modern);
        }
    }
}
