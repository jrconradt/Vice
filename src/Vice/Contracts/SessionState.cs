namespace Vice.Contracts;

public sealed class SessionState
{
    public const int ProtocolVersion = 2;

    public string AppName { get; }
    public string PipeName { get; }

    public SessionState(string appName, string? pipeName = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("appName must be non-empty.", nameof(appName));
        }

        AppName = appName;
        PipeName = pipeName ?? $"{appName}-session-v{ProtocolVersion}-{Environment.UserName}";
    }

    public static SessionState For(string appName, string? pipeName = null)
    {
        if (pipeName is null)
        {
            var overridePipe = Environment.GetEnvironmentVariable("VICE_PIPE_NAME");
            if (!string.IsNullOrWhiteSpace(overridePipe))
            {
                pipeName = overridePipe;
            }
        }

        return new SessionState(appName, pipeName);
    }
}
