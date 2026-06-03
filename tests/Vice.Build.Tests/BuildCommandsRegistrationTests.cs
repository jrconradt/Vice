using Vice.Build;
using Vice.Build.Dotnet;
using Vice.Core;
using Vice.Host;
using Xunit;

namespace Vice.Build.Tests;

public class BuildCommandsRegistrationTests
{
    [Fact]
    public async Task Register_WiresBuildVerbsIntoApp()
    {
        await using var queue = new DotnetBuildQueue();
        await using var app = (ViceApp)ViceApp.Create("vice-test", "0.0.0").Build();

        BuildCommands.Register(app, queue);

        var verbs = new[]
        {
            "build",
            "test",
            "restore",
            "clean",
        };
        foreach (var verb in verbs)
        {
            Assert.NotNull(app.Registry.FindByVerb(verb));
        }
    }

    [Fact]
    public async Task Queue_RegisteredAsSessionService_IsDrainedOnAppDispose()
    {
        var queue = new DotnetBuildQueue();
        var app = ViceApp.Create("vice-test", "0.0.0")
            .WithSessionService(queue)
            .Build();

        BuildCommands.Register(app, queue);

        await app.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queue.GetOrStart("k", () => Task.FromResult(0)));
    }
}
