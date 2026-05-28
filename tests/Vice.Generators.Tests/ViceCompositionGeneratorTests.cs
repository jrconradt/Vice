using Microsoft.CodeAnalysis;
using Xunit;

namespace Vice.Generators.Tests;

public class ViceCompositionGeneratorTests
{
    [Fact]
    public void HappyPath_EmitsComposeFromAttributes()
    {
        const string SOURCE = """
            using Vice.Composition;
            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices
            {
                public IMyService Svc { get; } = null!;
            }

            public interface IMyService { }

            internal static class Factories
            {
                [ViceSessionService]
                public static IMyService BuildSvc(IMyService svc) => svc;
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);
        var emitted = result.CombinedSource;
        Assert.Contains("static class ViceComposition", emitted);
        Assert.Contains("ComposeFromAttributes", emitted);
        Assert.Contains("RegisterDiscoveredPacks", emitted);
        Assert.Contains("namespace MyApp;", emitted);
        Assert.Contains("builder.WithSessionService", emitted);
    }

    [Fact]
    public void HappyPath_EmitsPackRegistration()
    {
        const string SOURCE = """
            using Vice.Composition;
            using Vice;
            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceCommandPack]
            internal static class MyPack
            {
                public static void Register(IViceApp app) { }
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var emitted = result.CombinedSource;
        Assert.Contains("MyApp.MyPack.Register(app);", emitted);
    }

    [Fact]
    public void VICE001_NoViceHostType_EmitsInfoDiagnostic()
    {
        const string SOURCE = """
            namespace MyApp;
            public class NotAHost { }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        var vice001 = result.GeneratorDiagnostics.FirstOrDefault(d => d.Id == "VICE001");
        Assert.NotNull(vice001);
        Assert.Equal(DiagnosticSeverity.Info, vice001!.Severity);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void VICE003_UnresolvedFactoryParameter_EmitsError()
    {
        const string SOURCE = """
            using Vice.Composition;
            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            public interface IFoo { }

            internal static class Factories
            {
                [ViceSessionService]
                public static IFoo Make(IFoo missing) => missing;
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        var vice003 = result.GeneratorDiagnostics.FirstOrDefault(d => d.Id == "VICE003");
        Assert.NotNull(vice003);
        Assert.Equal(DiagnosticSeverity.Error, vice003!.Severity);
    }

    [Fact]
    public void VICE005_DuplicateFactoryReturnType_EmitsError()
    {
        const string SOURCE = """
            using Vice.Composition;
            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            public interface IFoo { }
            public sealed class FooImpl : IFoo { }

            internal static class Factories
            {
                [ViceSessionService]
                public static IFoo MakeA() => new FooImpl();

                [ViceSessionService]
                public static IFoo MakeB() => new FooImpl();
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        var vice005 = result.GeneratorDiagnostics.FirstOrDefault(d => d.Id == "VICE005");
        Assert.NotNull(vice005);
        Assert.Equal(DiagnosticSeverity.Error, vice005!.Severity);
    }

    [Fact]
    public void VICE006_BadGlobalOptionType_EmitsError()
    {
        const string SOURCE = """
            using Vice.Composition;
            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceOption]
            public sealed class NotAnOption { }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        var vice006 = result.GeneratorDiagnostics.FirstOrDefault(d => d.Id == "VICE006");
        Assert.NotNull(vice006);
        Assert.Equal(DiagnosticSeverity.Error, vice006!.Severity);
    }
}
