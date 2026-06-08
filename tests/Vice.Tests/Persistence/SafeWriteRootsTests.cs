using Vice.Persistence;
using Xunit;

namespace Vice.Tests.Persistence;

[Collection("EnvVarSerial")]
public class SafeWriteRootsTests
{
    private readonly EnvVarSerialFixture _serial;

    public SafeWriteRootsTests(EnvVarSerialFixture serial)
    {
        _serial = serial;
    }

    [Fact]
    public void ExactRootPath_IsAllowed()
    {
        var root = SyntheticRoot();
        using var _ = new EnvScope(_serial, ("VICE_ALLOWED_ROOTS", root));
        var allowed = SafeWriteRoots.IsAllowed(root, out var reason);
        Assert.True(allowed, reason);
    }

    [Fact]
    public void PathStrictlyInsideRoot_IsAllowed()
    {
        var root = SyntheticRoot();
        using var _ = new EnvScope(_serial, ("VICE_ALLOWED_ROOTS", root));
        var target = Path.Combine(root, "sub", "file.bin");
        var allowed = SafeWriteRoots.IsAllowed(target, out var reason);
        Assert.True(allowed, reason);
    }

    [Fact]
    public void SiblingPrefixConfusionPath_IsRejected()
    {
        var root = SyntheticRoot();
        var siblingTarget = Path.Combine(root + "Sibling", "file.bin");
        using var _ = new EnvScope(_serial, ("VICE_ALLOWED_ROOTS", root));
        var allowed = SafeWriteRoots.IsAllowed(siblingTarget, out var reason);
        Assert.False(allowed);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void CurrentDirectory_IsAllowedByDefault()
    {
        var cwd = Directory.GetCurrentDirectory();
        var target = Path.Combine(cwd, $"vice-cwd-write-{Guid.NewGuid():N}.bin");
        var allowed = SafeWriteRoots.IsAllowed(target, out var reason);
        Assert.True(allowed, reason);
    }

    [Fact]
    public void ClearlyOutsidePath_IsRejectedWithReason()
    {
        var root = SyntheticRoot();
        using var _ = new EnvScope(_serial, ("VICE_ALLOWED_ROOTS", root));
        var outside = $"/vice-outside-root-{Guid.NewGuid():N}/file.bin";
        var allowed = SafeWriteRoots.IsAllowed(outside, out var reason);
        Assert.False(allowed);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    private static string SyntheticRoot()
        => $"/vice-allowed-root-{Guid.NewGuid():N}";
}
