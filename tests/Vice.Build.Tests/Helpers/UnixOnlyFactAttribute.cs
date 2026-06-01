using Xunit;

namespace Vice.Build.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: relies on POSIX process semantics not present on Windows.";
        }
    }
}
