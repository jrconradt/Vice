using System.Threading.Tasks;
using Vice.Commands;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class CommandRegistryCollisionTests
{
    [Fact]
    public void ValidateCollisions_ReturnsEntry_WhenTwoIdenticalChainsRegistered()
    {
        var registry = new CommandRegistry();
        registry.Register(verb("ping"), "first", (ctx, ct) => Task.FromResult(0));
        registry.Register(verb("ping"), "second", (ctx, ct) => Task.FromResult(1));

        var collisions = registry.ValidateCollisions();

        Assert.Single(collisions);
        Assert.Contains("ping", collisions[0]);
    }

    [Fact]
    public void ValidateCollisions_ReturnsEmpty_WhenChainsDiffer()
    {
        var registry = new CommandRegistry();
        registry.Register(verb("ping"), "ping", (ctx, ct) => Task.FromResult(0));
        registry.Register(verb("pong"), "pong", (ctx, ct) => Task.FromResult(0));

        var collisions = registry.ValidateCollisions();

        Assert.Empty(collisions);
    }

    [Fact]
    public void ValidateCollisions_ReturnsEmpty_WhenHeadIsSharedButTailDiffers()
    {
        var registry = new CommandRegistry();
        registry.Register(verb("grpc") > verb("list"), "list", (ctx, ct) => Task.FromResult(0));
        registry.Register(verb("grpc") > verb("call"), "call", (ctx, ct) => Task.FromResult(0));

        var collisions = registry.ValidateCollisions();

        Assert.Empty(collisions);
    }
}
