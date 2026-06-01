using Xunit;

namespace Vice.Files.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: relies on POSIX file modes / symlink semantics not present on Windows.";
        }
    }
}
