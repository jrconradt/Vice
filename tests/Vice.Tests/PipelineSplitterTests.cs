using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Parser;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class PipelineSplitterTests
{
    private static (CommandChain Chain, CommandRegistration Reg) Resolve(
        Action<CommandRegistry> register, params string[] args)
    {
        var registry = new CommandRegistry();
        register(registry);
        var result = CommandResolver.Resolve(args, registry.GetDescriptors(),
            valueBearingOptions: new HashSet<string>(),
            knownFlags: new HashSet<string>());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        return (result.Chain!, registry.Registrations[result.MatchedRegistrationIndex]);
    }

    [Fact]
    public void HasPipeline_FalseForFlatChain()
    {
        var (chain, _) = Resolve(r => r.Register(verb("a"), "a", (c, t) => Task.FromResult(0)),
            "a");

        Assert.False(PipelineSplitter.HasPipeline(chain));
    }

    [Fact]
    public void HasPipeline_TrueWhenPipingConjunctivePresent()
    {
        var (chain, _) = Resolve(r => r.RegisterPipeline(
                verb("a") > Connectors.Then() > noun("b"), "p",
                (c, t) => Task.FromResult(0), new()),
            "a", "then", "b");

        Assert.True(PipelineSplitter.HasPipeline(chain));
    }

    [Fact]
    public void Split_OnePiping_ProducesTwoStages()
    {
        var (chain, reg) = Resolve(r => r.RegisterPipeline(
                verb("a") > Connectors.Then() > noun("b"), "p",
                (c, t) => Task.FromResult(0), new()),
            "a", "then", "b");

        var stages = PipelineSplitter.Split(chain, reg);
        Assert.Equal(2, stages.Count);
        Assert.Null(stages[0].OperatorWord);
        Assert.Equal("then", stages[1].OperatorWord);
    }

    [Fact]
    public void Split_TwoPipings_ProducesThreeStages()
    {
        var (chain, reg) = Resolve(r => r.RegisterPipeline(
                verb("a") > Connectors.Then() > noun("b") > Connectors.Then() > noun("c"), "p",
                (c, t) => Task.FromResult(0), new()),
            "a", "then", "b", "then", "c");

        var stages = PipelineSplitter.Split(chain, reg);
        Assert.Equal(3, stages.Count);
    }

    [Fact]
    public async Task Split_AssignsPerStageHandlers_WhenRegistered()
    {
        var handler0Called = false;
        var handler1Called = false;

        var (chain, reg) = Resolve(r => r.RegisterPipeline(
                verb("a") > Connectors.Then() > noun("b"), "p",
                defaultHandler: (c, t) => Task.FromResult(0),
                stageHandlers: new()
                {
                    [0] = (c, t) => { handler0Called = true; return Task.FromResult(0); },
                    [1] = (c, t) => { handler1Called = true; return Task.FromResult(0); }
                }),
            "a", "then", "b");

        var stages = PipelineSplitter.Split(chain, reg);
        await stages[0].Handler(null!, default);
        await stages[1].Handler(null!, default);

        Assert.True(handler0Called);
        Assert.True(handler1Called);
    }
}
