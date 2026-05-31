using Vice.Build.Dotnet;
using Xunit;

namespace Vice.Build.Tests;

public class DotnetBuildQueueTests
{
    [Fact]
    public async Task ConcurrentSameKey_SharesSingleFactoryInvocation()
    {
        await using var queue = new DotnetBuildQueue();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        Func<Task<int>> factory = () =>
        {
            Interlocked.Increment(ref invocations);
            return gate.Task;
        };

        var first = queue.GetOrStart("k", factory);
        var second = queue.GetOrStart("k", factory);

        Assert.Equal(1, Volatile.Read(ref invocations));

        gate.SetResult(42);
        Assert.Equal(42, await first);
        Assert.Equal(42, await second);
        Assert.Equal(1, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task DifferentKeys_RunIndependently()
    {
        await using var queue = new DotnetBuildQueue();
        var invocations = 0;

        Func<Task<int>> factory = () =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(0);
        };

        await queue.GetOrStart("a", factory);
        await queue.GetOrStart("b", factory);

        Assert.Equal(2, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task CompletedKey_ReRunsOnNextCall()
    {
        await using var queue = new DotnetBuildQueue();
        var invocations = 0;

        Func<Task<int>> factory = () =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(7);
        };

        Assert.Equal(7, await queue.GetOrStart("k", factory));
        Assert.Equal(7, await queue.GetOrStart("k", factory));

        Assert.Equal(2, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task FaultingFactory_RemovesKeyAndAllowsRetry()
    {
        await using var queue = new DotnetBuildQueue();
        var invocations = 0;

        Func<Task<int>> faulting = () =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromException<int>(new InvalidOperationException("boom"));
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.GetOrStart("k", faulting));

        Func<Task<int>> succeeding = () =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(3);
        };

        Assert.Equal(3, await queue.GetOrStart("k", succeeding));
        Assert.Equal(2, Volatile.Read(ref invocations));
    }

    [Fact]
    public async Task GetOrStart_AfterDispose_Throws()
    {
        var queue = new DotnetBuildQueue();
        await queue.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queue.GetOrStart("k", () => Task.FromResult(0)));
    }

    [Fact]
    public async Task Dispose_DrainsInFlightBuild()
    {
        var queue = new DotnetBuildQueue();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        Func<Task<int>> factory = async () =>
        {
            await gate.Task;
            completed = true;
            return 0;
        };

        var inflight = queue.GetOrStart("k", factory);

        var dispose = queue.DisposeAsync();
        Assert.False(dispose.IsCompleted);

        gate.SetResult(0);
        await dispose;

        Assert.True(completed);
        Assert.Equal(0, await inflight);
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var queue = new DotnetBuildQueue();
        await queue.DisposeAsync();
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task KeysAreCaseInsensitive()
    {
        await using var queue = new DotnetBuildQueue();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        Func<Task<int>> factory = () =>
        {
            Interlocked.Increment(ref invocations);
            return gate.Task;
        };

        var first = queue.GetOrStart("Build::/Repo", factory);
        var second = queue.GetOrStart("build::/repo", factory);

        Assert.Equal(1, Volatile.Read(ref invocations));

        gate.SetResult(0);
        await first;
        await second;
    }
}
