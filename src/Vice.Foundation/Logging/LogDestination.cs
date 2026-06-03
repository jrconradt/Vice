namespace Vice.Logging;

public static class LogDestination
{
    public const string FILE_VARIABLE = "VICE_LOG_FILE";
    public const long MAX_LOG_BYTES = 64L * 1024 * 1024;
    public const int RETAINED_GENERATIONS = 3;

    public static TextWriter? Resolve(bool daemon, string programName)
    {
        var path = Environment.GetEnvironmentVariable(FILE_VARIABLE);
        if (string.IsNullOrWhiteSpace(path) && daemon)
        {
            path = DefaultDaemonPath(programName);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var resolved = path.Trim();
        try
        {
            var directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(directory))
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                    || OperatingSystem.IsFreeBSD())
                {
                    Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                else
                {
                    Directory.CreateDirectory(directory);
                }
            }

            RotateIfOversized(resolved);

            Vice.Persistence.FileAccessControl.RestrictToCurrentUser(resolved);

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

            var stream = new FileStream(resolved, options);
            return new StreamWriter(stream)
            {
                AutoFlush = true,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"{programName}: cannot open {FILE_VARIABLE} '{resolved}' ({ex.GetType().Name}), logging to stderr.");
            return null;
        }
    }

    private static void RotateIfOversized(string resolved)
    {
        try
        {
            var info = new FileInfo(resolved);
            if (!info.Exists || info.Length < MAX_LOG_BYTES)
            {
                return;
            }

            for (var generation = RETAINED_GENERATIONS - 1; generation >= 1; generation--)
            {
                var older = $"{resolved}.{generation}";
                var newer = $"{resolved}.{generation + 1}";
                if (File.Exists(older))
                {
                    File.Move(older, newer, overwrite: true);
                }
            }

            File.Move(resolved, $"{resolved}.1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static string DefaultDaemonPath(string programName)
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrWhiteSpace(stateHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vice")
            : Path.Combine(stateHome.Trim(), "vice");
        return Path.Combine(baseDir, $"{programName}-daemon.log");
    }
}
