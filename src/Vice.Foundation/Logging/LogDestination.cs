namespace Vice.Logging;

public static class LogDestination
{
    public const string FILE_VARIABLE = "VICE_LOG_FILE";

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
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(resolved,
                                        FileMode.Append,
                                        FileAccess.Write,
                                        FileShare.ReadWrite);
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

    private static string DefaultDaemonPath(string programName)
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrWhiteSpace(stateHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vice")
            : Path.Combine(stateHome.Trim(), "vice");
        return Path.Combine(baseDir, $"{programName}-daemon.log");
    }
}
