using Vice.Lexicon;
using Vice.Nodes;
using Vice.Parser;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class DslTests
{
    [Fact]
    public void Verb_CreatesWordNode()
    {
        var node = verb("add");
        Assert.Equal("add", node.Name);
        Assert.Equal(ChainNodeKind.Word, node.Kind);
    }

    [Fact]
    public void Verb_WithSynonyms()
    {
        var node = verb("add", "create", "new");
        Assert.Contains("create", node.SynonymList);
        Assert.Contains("new", node.SynonymList);
    }

    [Fact]
    public void Chain_OpLinksNodes()
    {
        var chain = verb("add") > noun("user");
        Assert.Equal("add", chain.Name);
        Assert.NotNull(chain.NextNode);
        Assert.Equal("user", chain.NextNode!.Name);
    }

    [Fact]
    public void Chain_MultiLevel()
    {
        var chain = verb("add") > noun("user") > Connectors.To();
        Assert.Equal("add", chain.Name);
        Assert.Equal("user", chain.NextNode!.Name);
        Assert.Equal("to", chain.NextNode!.NextNode!.Name);
    }

    [Fact]
    public void Synonym_Op_AddsToLeft()
    {
        var node = verb("add") | noun("create");
        Assert.Contains("create", node.SynonymList);
    }

    [Fact]
    public void Target_Op_AttachesToTail()
    {
        var chain = verb("add") * target("name");
        Assert.Single(chain.TargetList);
        Assert.Equal("name", chain.TargetList[0].Name);
    }

    [Fact]
    public void Target_Op_AttachesToChainTail()
    {
        var chain = verb("add") > noun("user") * target("name");

        Assert.Empty(chain.TargetList);
        Assert.Single(chain.NextNode!.TargetList);
        Assert.Equal("name", chain.NextNode!.TargetList[0].Name);
    }

    [Fact]
    public void Conjunctive_IsRelational_ByDefault()
    {
        Assert.Equal(ConjunctiveKind.Relational, Connectors.To().ConjunctiveKind);
        Assert.Equal(ConjunctiveKind.Relational, Connectors.With().ConjunctiveKind);
    }

    [Fact]
    public void Conjunctive_Then_IsPiping()
    {
        Assert.Equal(ConjunctiveKind.Piping, Connectors.Then().ConjunctiveKind);
        Assert.Equal(ConjunctiveKind.Piping, Connectors.AndPipe().ConjunctiveKind);
    }

    [Fact]
    public void Chain_ClonesConjunctive_DoesNotMutateShared()
    {
        var chain1 = verb("add") > Connectors.To() > noun("group");
        var chain2 = verb("delete") > Connectors.To() > noun("group");

        Assert.Null(((ChainNode)Connectors.To()).NextNode);
    }

    [Fact]
    public void optional_helper_wraps_chain_in_OptionalNode()
    {
        var node = optional(verb("foo"));
        var opt = Assert.IsType<OptionalNode>(node);
        Assert.Equal("foo", opt.Inner.Name);
        Assert.Equal(ChainNodeKind.Optional, opt.Kind);
    }

    [Fact]
    public void optional_helper_preserves_inner_via_Clone()
    {
        var original = (OptionalNode)optional(verb("foo", "bar"));
        var clone = (OptionalNode)original.Clone();
        clone.Inner.SynonymList.Add("baz");
        Assert.DoesNotContain("baz", original.Inner.SynonymList);
    }

    [Fact]
    public void oneOf_helper_requires_two_or_more_alternatives()
    {
        Assert.Throws<ArgumentException>(() => oneOf(verb("a")));
    }

    [Fact]
    public void oneOf_helper_exposes_alternatives_in_order()
    {
        var node = oneOf(verb("x"), verb("y"), verb("z"));
        var alt = Assert.IsType<AlternationNode>(node);
        Assert.Equal(3, alt.Alternatives.Count);
        Assert.Equal("x", alt.Alternatives[0].Name);
        Assert.Equal("y", alt.Alternatives[1].Name);
        Assert.Equal("z", alt.Alternatives[2].Name);
    }

    [Fact]
    public void oneOf_helper_kind_is_Alternation()
    {
        var node = oneOf(verb("x"), verb("y"));
        Assert.Equal(ChainNodeKind.Alternation, node.Kind);
    }

    [Fact]
    public void repeat_helper_defaults_to_min_0_max_unbounded()
    {
        var node = repeat(verb("foo"));
        var rep = Assert.IsType<RepetitionNode>(node);
        Assert.Equal(0, rep.Min);
        Assert.Equal(int.MaxValue, rep.Max);
        Assert.Null(rep.Separator);
    }

    [Fact]
    public void repeat_helper_with_separator_assigns_separator()
    {
        var node = repeat(verb("x"), separator: verb("and"));
        var rep = Assert.IsType<RepetitionNode>(node);
        Assert.NotNull(rep.Separator);
        Assert.Equal("and", rep.Separator!.Name);
    }

    [Fact]
    public void repeat_helper_rejects_negative_min()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => repeat(verb("x"), min: -1));
    }

    [Fact]
    public void repeat_helper_rejects_max_less_than_min()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => repeat(verb("x"), min: 5, max: 2));
    }
}
