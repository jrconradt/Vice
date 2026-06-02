using Xunit;

namespace Vice.Net.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveNetFactAttribute : FactAttribute
{
    public const string ENV_VAR = "VICE_LIVE_NET";

    public LiveNetFactAttribute()
    {
        if (!Enabled())
        {
            Skip = $"Live network test: set {ENV_VAR}=1 to run against real third-party research APIs.";
        }
    }

    private static bool Enabled()
    {
        var value = Environment.GetEnvironmentVariable(ENV_VAR);
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value != "0"
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }
}
