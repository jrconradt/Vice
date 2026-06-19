using Vice.Build;
using Vice.Host;
using Xunit;

namespace Vice.Build.Tests;

public class BuildCommandsRegistrationTests
{
    [Fact]
    public async Task Register_WiresBuildVerbsIntoApp()
    {
        await using var app = (ViceApp)ViceApp.Create("vice-test", "0.0.0").Build();

        BuildCommands.Register(app);

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
}
