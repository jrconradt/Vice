using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Vice.Concurrency;
using Xunit;

namespace Vice.Tests;

public class SerialQueueTests
{
    [Fact]
    public async Task EnqueueAfterDisposeThrowsObjectDisposedException()
    {
        var queue = new SerialQueue();
        await queue.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => queue.EnqueueAsync(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task WorkRunsInSubmissionOrder()
    {
        await using var queue = new SerialQueue();
        var observed = new ConcurrentQueue<int>();
        var tasks = new Task[64];
        for (var i = 0; i < tasks.Length; i++)
        {
            var index = i;
            tasks[i] = queue.EnqueueAsync(async ct =>
            {
                await Task.Yield();
                observed.Enqueue(index);
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(64, observed.Count);
        var expected = 0;
        foreach (var value in observed)
        {
            Assert.Equal(expected, value);
            expected++;
        }
    }

    [Fact]
    public async Task ExceptionInWorkPropagatesToCaller()
    {
        await using var queue = new SerialQueue();
        var failure = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync<int>(_ => throw failure));

        Assert.Same(failure, thrown);
    }
}
