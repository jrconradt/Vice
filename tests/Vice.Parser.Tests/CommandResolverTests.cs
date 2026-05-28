using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

file sealed class TestDescriptor : IChainDescriptor
{
    public string Name { get; }
    public ChainNodeKind Kind { get; }
    public ConjunctiveKind? ConjunctiveKind { get; }
    public IReadOnlyList<string> Synonyms { get; }
    public IReadOnlyList<ITargetDescriptor> Targets { get; }
    public IChainDescriptor? Next { get; }

    public TestDescriptor(
        string name,
        ChainNodeKind kind = ChainNodeKind.Word,
        ConjunctiveKind? conjunctiveKind = null,
        string[]? synonyms = null,
        ITargetDescriptor[]? targets = null,
        IChainDescriptor? next = null)
    {
        Name = name;
        Kind = kind;
        ConjunctiveKind = conjunctiveKind;
        Synonyms = synonyms ?? [];
        Targets = targets ?? [];
        Next = next;
    }
}

file sealed class TestTarget : ITargetDescriptor
{
    public string Name { get; }
    public bool Required { get; }
    public bool Variadic { get; }

    public TestTarget(string name, bool required = true, bool variadic = false)
    {
        Name = name;
        Required = required;
        Variadic = variadic;
    }
}

public class CommandResolverTests
{
    private static IChainDescriptor Word(string name, string[]? synonyms = null,
        ITargetDescriptor[]? targets = null, IChainDescriptor? next = null)
        => new TestDescriptor(name, ChainNodeKind.Word, null, synonyms, targets, next);

    private static IChainDescriptor Conj(string name, ITargetDescriptor[]? targets = null, IChainDescriptor? next = null)
        => new TestDescriptor(name, ChainNodeKind.Conjunctive, Parser.ConjunctiveKind.Relational, null, targets, next);

    private static ITargetDescriptor Target(string name, bool required = true)
        => new TestTarget(name, required);

    [Fact]
    public void SimpleVerb_Matches()
    {
        var reg = new[] { Word("add") };
        var result = CommandResolver.Resolve(["add"], reg);
        Assert.True(result.Success);
        Assert.Single(result.Chain!.Nodes);
        Assert.Equal("add", result.Chain.Nodes[0].MatchedName);
    }

    [Fact]
    public void UnknownVerb_Fails()
    {
        var reg = new[] { Word("add") };
        var result = CommandResolver.Resolve(["delete"], reg);
        Assert.False(result.Success);
        Assert.Contains("Unknown command", result.Errors[0]);
    }

    [Fact]
    public void Synonym_Matches()
    {
        var reg = new[] { Word("add", synonyms: ["create", "new"]) };
        var result = CommandResolver.Resolve(["create"], reg);
        Assert.True(result.Success);
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        var reg = new[] { Word("Add") };
        var result = CommandResolver.Resolve(["ADD"], reg);
        Assert.True(result.Success);
    }

    [Fact]
    public void Target_Captured()
    {
        var reg = new[] { Word("add", targets: [Target("name")]) };
        var result = CommandResolver.Resolve(["add", "jeff"], reg);
        Assert.True(result.Success);
        Assert.Equal("jeff", result.Chain!.Nodes[0].TargetValues["name"]);
    }

    [Fact]
    public void RequiredTarget_Missing_Fails()
    {
        var reg = new[] { Word("add", targets: [Target("name", required: true)]) };
        var result = CommandResolver.Resolve(["add"], reg);
        Assert.False(result.Success);
    }

    [Fact]
    public void OptionalTarget_Missing_Succeeds()
    {
        var reg = new[] { Word("add", targets: [Target("name", required: false)]) };
        var result = CommandResolver.Resolve(["add"], reg);
        Assert.True(result.Success);
    }

    [Fact]
    public void ChainWithConjunctive_Matches()
    {

        var conj = new TestDescriptor("to", ChainNodeKind.Conjunctive,
            Parser.ConjunctiveKind.Relational, null, [Target("groupname")], null);
        var reg = new[] { Word("add", targets: [Target("username")], next: conj) };

        var result = CommandResolver.Resolve(["add", "jeff", "to", "admins"], reg);
        Assert.True(result.Success);
        var values = result.Chain!.AllTargetValues();
        Assert.Equal("jeff", values["username"]);
        Assert.Equal("admins", values["groupname"]);
    }

    [Fact]
    public void ExtraTokens_Fails()
    {
        var reg = new[] { Word("add") };
        var result = CommandResolver.Resolve(["add", "extra"], reg);
        Assert.False(result.Success);
    }

    [Fact]
    public void EmptyArgs_Fails()
    {
        var reg = new[] { Word("add") };
        var result = CommandResolver.Resolve([], reg);
        Assert.False(result.Success);
    }

    [Fact]
    public void MatchedRegistrationIndex_IsCorrect()
    {
        var reg = new IChainDescriptor[] { Word("add"), Word("delete") };
        var result = CommandResolver.Resolve(["delete"], reg);
        Assert.True(result.Success);
        Assert.Equal(1, result.MatchedRegistrationIndex);
    }
}
