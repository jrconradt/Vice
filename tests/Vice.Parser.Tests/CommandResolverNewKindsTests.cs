using Vice.Nodes;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Parser.Tests;

public class CommandResolverNewKindsTests
{
    private static IReadOnlyList<IChainDescriptor> Reg(params ChainNode[] roots)
    {
        var list = new List<IChainDescriptor>(roots.Length);
        foreach (var n in roots)
        {
            list.Add(n);
        }

        return list;
    }

    [Fact]
    public void Optional_node_matches_when_inner_chain_matches()
    {
        var chain = verb("foo") > optional(verb("bar")) > verb("baz");
        var result = CommandResolver.Resolve(["foo", "bar", "baz"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Optional_node_skips_when_inner_chain_does_not_match()
    {
        var chain = verb("foo") > optional(verb("bar")) > verb("baz");
        var result = CommandResolver.Resolve(["foo", "baz"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Optional_node_at_end_of_chain_succeeds_when_tokens_exhausted()
    {
        var chain = verb("foo") > optional(verb("bar"));
        var result = CommandResolver.Resolve(["foo"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Optional_with_target_inside_captures_when_present()
    {
        var chain = verb("foo") > optional(verb("with") * target("x"));
        var result = CommandResolver.Resolve(["foo", "with", "thing"], Reg(chain));
        Assert.True(result.Success);
        var values = result.Chain!.AllTargetValues();
        Assert.Equal("thing", values["x"]);
    }

    [Fact]
    public void Alternation_matches_first_alternative()
    {
        var chain = verb("foo") > oneOf(verb("a"), verb("b"));
        var result = CommandResolver.Resolve(["foo", "a"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Alternation_matches_second_alternative()
    {
        var chain = verb("foo") > oneOf(verb("a"), verb("b"));
        var result = CommandResolver.Resolve(["foo", "b"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Alternation_fails_when_no_alternative_matches()
    {
        var chain = verb("foo") > oneOf(verb("a"), verb("b"));
        var result = CommandResolver.Resolve(["foo", "c"], Reg(chain));
        Assert.False(result.Success);
    }

    [Fact]
    public void Alternation_with_tail_continues_after_chosen_alternative_first()
    {
        var chain = verb("foo") > oneOf(verb("a"), verb("b")) > verb("end");
        var result = CommandResolver.Resolve(["foo", "a", "end"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Alternation_with_tail_continues_after_chosen_alternative_second()
    {
        var chain = verb("foo") > oneOf(verb("a"), verb("b")) > verb("end");
        var result = CommandResolver.Resolve(["foo", "b", "end"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Repetition_min_1_matches_one()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 1, max: 5);
        var result = CommandResolver.Resolve(["foo", "x"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Repetition_min_1_matches_three()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 1, max: 5);
        var result = CommandResolver.Resolve(["foo", "x", "x", "x"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Repetition_min_1_fails_when_zero_matches()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 1, max: 5);
        var result = CommandResolver.Resolve(["foo"], Reg(chain));
        Assert.False(result.Success);
    }

    [Fact]
    public void Repetition_min_0_succeeds_when_zero_matches()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 0, max: 5);
        var result = CommandResolver.Resolve(["foo"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Repetition_with_separator_consumes_separator_between_items()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 1, max: 5, separator: verb(","));
        var result = CommandResolver.Resolve(["foo", "x", ",", "x", ",", "x"], Reg(chain));
        Assert.True(result.Success);
    }

    [Fact]
    public void Repetition_respects_max_bound()
    {
        var chain = verb("foo") > repeat(verb("x"), min: 0, max: 2);
        var result = CommandResolver.Resolve(["foo", "x", "x", "x"], Reg(chain));
        Assert.False(result.Success);
    }

    [Fact]
    public void Partials_disabled_by_default_falls_back_to_exact_or_synonym()
    {
        var chain = verb("download");
        var result = CommandResolver.Resolve(["down"], Reg(chain), null, null, partialsEnabled: false);
        Assert.False(result.Success);
    }

    [Fact]
    public void Partials_enabled_unique_prefix_matches()
    {
        var chain = verb("download");
        var result = CommandResolver.Resolve(["down"], Reg(chain), null, null, partialsEnabled: true);
        Assert.True(result.Success);
    }

    [Fact]
    public void Partials_enabled_ambiguous_prefix_reports_candidates()
    {
        var result = CommandResolver.Resolve(["s"], Reg(verb("search"), verb("set")), null, null, partialsEnabled: true);
        Assert.False(result.Success);
        Assert.NotNull(result.BestMatch);
        var candidates = result.BestMatch!.AmbiguousCandidates;
        Assert.NotNull(candidates);
        Assert.Contains("search", candidates!);
        Assert.Contains("set", candidates!);
    }

    [Fact]
    public void Partials_enabled_exact_match_takes_precedence_over_prefix()
    {
        var result = CommandResolver.Resolve(["set"], Reg(verb("set"), verb("setup")), null, null, partialsEnabled: true);
        Assert.True(result.Success);
        Assert.Equal("set", result.Chain!.Nodes[0].MatchedName);
    }

    [Fact]
    public void Partials_only_applies_at_head()
    {
        var chain = verb("foo") > verb("bar");
        var result = CommandResolver.Resolve(["f", "b"], Reg(chain), null, null, partialsEnabled: true);
        Assert.False(result.Success);
    }

    [Fact]
    public void Implicit_pipeline_chains_two_verbs_without_explicit_then()
    {
        var read = verb("read") * target("path");
        var write = verb("write") > verb("to") > verb("file") * target("path");
        var result = CommandResolver.Resolve(["read", "a.txt", "write", "to", "file", "b.txt"], Reg(read, write));
        Assert.True(result.Success);
        Assert.NotNull(result.Segments);
        Assert.Equal(2, result.Segments!.Count);
        Assert.Equal("then", result.Segments[1].OperatorWord);
    }

    [Fact]
    public void Implicit_pipeline_explicit_then_still_resolves_two_stages()
    {
        var read = verb("read") * target("path");
        var write = verb("write") > verb("to") > verb("file") * target("path");
        var result = CommandResolver.Resolve(["read", "a.txt", "then", "write", "to", "file", "b.txt"], Reg(read, write));
        Assert.True(result.Success);
        Assert.NotNull(result.Segments);
        Assert.Equal(2, result.Segments!.Count);
        Assert.Equal("then", result.Segments[1].OperatorWord);
    }

    [Fact]
    public void Implicit_pipeline_fails_when_extra_token_is_not_a_verb_head()
    {
        var read = verb("read") * target("path");
        var result = CommandResolver.Resolve(["read", "a.txt", "junk"], Reg(read));
        Assert.False(result.Success);
    }

    [Fact]
    public void Implicit_pipelines_disabled_preserves_strict_behavior()
    {
        var read = verb("read") * target("path");
        var write = verb("write") > verb("to") > verb("file") * target("path");
        var result = CommandResolver.Resolve(
            ["read", "a.txt", "write", "to", "file", "b.txt"],
            Reg(read, write),
            null, null,
            partialsEnabled: true,
            implicitPipelinesEnabled: false);
        Assert.False(result.Success);
    }
}
