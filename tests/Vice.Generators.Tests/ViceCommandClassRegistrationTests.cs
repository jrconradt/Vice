using Microsoft.CodeAnalysis;
using Vice.Contracts;
using Xunit;

namespace Vice.Generators.Tests;

public class ViceCommandClassRegistrationTests
{
    [Fact]
    public void ClassCommand_WithExplicitVerb_EmitsRegistration()
    {
        const string SOURCE = """
            using System.Threading;
            using System.Threading.Tasks;
            using Vice.Composition;
            using Vice.Execution;

            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceCommand(Verb = "outdated", Description = "list outdated")]
            public sealed partial class OutdatedCommand : IViceCommand
            {
                public Task<int> Handle(CommandContext ctx, CancellationToken ct) => Task.FromResult(0);
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var emitted = result.CombinedSource;
        Assert.Contains("ViceCommandRegistration.Register<global::MyApp.OutdatedCommand>", emitted);
        Assert.Contains("global::Vice.Core.Dsl.verb(\"outdated\")", emitted);
        Assert.Contains("\"list outdated\"", emitted);
    }

    [Fact]
    public void ClassCommand_WithoutVerb_DerivesKebabFromTypeName()
    {
        const string SOURCE = """
            using System.Threading;
            using System.Threading.Tasks;
            using Vice.Composition;
            using Vice.Execution;

            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceCommand]
            public sealed partial class CheckOutdatedCommand : IViceCommand
            {
                public Task<int> Handle(CommandContext ctx, CancellationToken ct) => Task.FromResult(0);
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var emitted = result.CombinedSource;
        Assert.Contains("global::Vice.Core.Dsl.verb(\"check-outdated\")", emitted);
    }

    [Fact]
    public void ClassCommand_WithExplicitTargets_EmitsTargetSetPartial()
    {
        const string SOURCE = """
            using System.Threading;
            using System.Threading.Tasks;
            using Vice.Composition;
            using Vice.Execution;

            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceCommand("project", Verb = "outdated")]
            public sealed partial class OutdatedCommand : IViceCommand
            {
                public Task<int> Handle(CommandContext ctx, CancellationToken ct) => Task.FromResult(0);
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var emitted = result.CombinedSource;
        Assert.Contains("partial class OutdatedCommand", emitted);
        Assert.Contains("global::Vice.Composition.IViceCommand", emitted);
        Assert.Contains("TargetSet Targets(", emitted);
        Assert.Contains("public string Project => _ctx[\"project\"]", emitted);
        Assert.Contains("global::Vice.Core.Dsl.target(\"project\")", emitted);
    }

    [Fact]
    public void ClassCommand_NotImplementingInterface_IsNotRegistered()
    {
        const string SOURCE = """
            using System.Threading;
            using System.Threading.Tasks;
            using Vice.Composition;
            using Vice.Execution;

            namespace MyApp;

            [ViceHost]
            internal sealed class HostServices { }

            [ViceCommand(Verb = "loose")]
            public sealed partial class LooseCommand
            {
                public Task<int> Handle(CommandContext ctx, CancellationToken ct) => Task.FromResult(0);
            }
            """;

        var result = GeneratorHarness.Run(SOURCE);

        var emitted = result.CombinedSource;
        Assert.DoesNotContain("ViceCommandRegistration.Register<global::MyApp.LooseCommand>", emitted);
    }
}
