using System.Globalization;
using Vice.Host.Core;
using Xunit;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class SystemdNotifyWatchdogTests
{
    private readonly EnvVarSerialFixture _serial;

    public SystemdNotifyWatchdogTests(EnvVarSerialFixture serial)
    {
        _serial = serial;
    }

    [Fact]
    public void WatchdogInterval_WhenUsecSetAndPidMatches_ReturnsHalfInterval()
    {
        const long RAW_USEC = 10_000_000L;
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", RAW_USEC.ToString(CultureInfo.InvariantCulture)),
                                   ("WATCHDOG_PID", pid));
        var expected = TimeSpan.FromMilliseconds(RAW_USEC / 1000.0 / 2.0);
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WatchdogInterval_WhenUsecSetAndPidAbsent_ReturnsHalfInterval()
    {
        const long RAW_USEC = 4_000_000L;
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", RAW_USEC.ToString(CultureInfo.InvariantCulture)),
                                   ("WATCHDOG_PID", null));
        var expected = TimeSpan.FromMilliseconds(RAW_USEC / 1000.0 / 2.0);
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WatchdogInterval_WhenPidDoesNotMatch_ReturnsNull()
    {
        var foreignPid = unchecked(Environment.ProcessId + 1).ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", "10000000"),
                                   ("WATCHDOG_PID", foreignPid));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }

    [Fact]
    public void WatchdogInterval_WhenPidNonNumeric_ReturnsNull()
    {
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", "10000000"),
                                   ("WATCHDOG_PID", "not-a-pid"));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }

    [Fact]
    public void WatchdogInterval_WhenUsecAbsent_ReturnsNull()
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", null),
                                   ("WATCHDOG_PID", pid));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }

    [Fact]
    public void WatchdogInterval_WhenUsecNonNumeric_ReturnsNull()
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", "abc"),
                                   ("WATCHDOG_PID", pid));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }

    [Fact]
    public void WatchdogInterval_WhenUsecZero_ReturnsNull()
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", "0"),
                                   ("WATCHDOG_PID", pid));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }

    [Fact]
    public void WatchdogInterval_WhenUsecNegative_ReturnsNull()
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var _ = new EnvScope(_serial,
                                   ("WATCHDOG_USEC", "-5000000"),
                                   ("WATCHDOG_PID", pid));
        var actual = SystemdNotify.WatchdogInterval();
        Assert.Null(actual);
    }
}
