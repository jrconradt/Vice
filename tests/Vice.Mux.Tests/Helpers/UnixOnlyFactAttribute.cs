using Xunit;

namespace Vice.Mux.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: relies on the 'tee' command and POSIX process semantics not present on Windows.";
        }
    }
}
