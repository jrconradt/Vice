using CsCheck;
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

public class CommandResolverPropertyTests
{
    private sealed record NodeSpec(
        string Name,
        bool IsConjunctive,
        string[] Synonyms,
        TargetSpec[] Targets);

    private sealed record TargetSpec(string Name, bool Required, bool Variadic);

    private const long ITERATIONS = 10_000;

    private static readonly string[] HeadWords =
        ["add", "remove", "search", "list", "show", "run", "open", "close"];

    private static readonly string[] ConjWords =
        ["to", "from", "on", "with", "into", "by"];

    private static readonly string[] SynonymWords =
        ["create", "new", "delete", "find", "display", "exec"];

    private static readonly string[] TargetNames =
        ["name", "value", "source", "group", "path", "id"];

    private static readonly string[] PipingWords =
        ["then", "and", "or", "pipe", "send"];

    private static readonly Gen<string> WordChar =
        Gen.Char["abAB09._"].Select(c => $"{c}");

    private static readonly Gen<string> RandomWord =
        WordChar.Array[1, 8].Select(parts => string.Concat(parts));

    private static readonly Gen<string> TokenWord =
        Gen.Frequency(
            (5, RandomWord),
            (2, Gen.OneOfConst(HeadWords)),
            (2, Gen.OneOfConst(ConjWords)),
            (1, Gen.OneOfConst(SynonymWords)),
            (2, Gen.OneOfConst(PipingWords)));

    private static readonly Gen<string[]> TokenStrings =
        TokenWord.Array[0, 8];

    private static readonly Gen<string> StructuralInput =
        Gen.String[Gen.Char[" \"',-abAB"], 0, 24];

    private static readonly Gen<TargetSpec> TargetSpecGen =
        Gen.Select(
            Gen.OneOfConst(TargetNames),
            Gen.Bool,
            Gen.Bool,
            (name, required, variadic) => new TargetSpec(name, required, variadic));

    private static readonly Gen<NodeSpec> WordNodeGen =
        Gen.Select(
            Gen.OneOfConst(HeadWords),
            Gen.OneOfConst(SynonymWords).Array[0, 2],
            TargetSpecGen.Array[0, 2],
            (name, synonyms, targets) => new NodeSpec(name, false, synonyms, targets));

    private static readonly Gen<NodeSpec> ConjNodeGen =
        Gen.Select(
            Gen.OneOfConst(ConjWords),
            TargetSpecGen.Array[0, 2],
            (name, targets) => new NodeSpec(name, true, [], targets));

    private static readonly Gen<NodeSpec[]> ChainSpecGen =
        Gen.Frequency(
            (3, WordNodeGen),
            (1, ConjNodeGen)).Array[1, 4];

    private static readonly Gen<NodeSpec[][]> RegistrationSpecGen =
        ChainSpecGen.Array[1, 4];

    private static ITargetDescriptor BuildTarget(TargetSpec spec)
    {
        return new TestTarget(spec.Name, spec.Required, spec.Variadic);
    }

    private static IChainDescriptor BuildNode(NodeSpec spec, IChainDescriptor? next)
    {
        var targets = new ITargetDescriptor[spec.Targets.Length];
        for (int t = 0; t < spec.Targets.Length; t++)
        {
            targets[t] = BuildTarget(spec.Targets[t]);
        }

        if (spec.IsConjunctive)
        {
            return new TestDescriptor(
                spec.Name,
                ChainNodeKind.Conjunctive,
                Vice.Parser.ConjunctiveKind.Preposition,
                null,
                targets,
                next);
        }

        return new TestDescriptor(
            spec.Name,
            ChainNodeKind.Word,
            null,
            spec.Synonyms,
            targets,
            next);
    }

    private static IChainDescriptor BuildChain(NodeSpec[] nodes)
    {
        var head = BuildNode(nodes[^1], null);
        for (int i = nodes.Length - 2; i >= 0; i--)
        {
            head = BuildNode(nodes[i], head);
        }

        return head;
    }

    private static IReadOnlyList<IChainDescriptor> BuildRegistrations(NodeSpec[][] chains)
    {
        var registrations = new IChainDescriptor[chains.Length];
        for (int i = 0; i < chains.Length; i++)
        {
            registrations[i] = BuildChain(chains[i]);
        }

        return registrations;
    }

    private static bool MatchesVocabulary(IChainDescriptor descriptor, string matchedName)
    {
        if (string.Equals(matchedName, descriptor.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var synonym in descriptor.Synonyms)
        {
            if (string.Equals(matchedName, synonym, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Resolve_RandomTreeAndTokens_NeverThrows()
    {
        Gen.Select(RegistrationSpecGen, TokenStrings).Sample(pair =>
            {
                var (chains, args) = pair;
                var registrations = BuildRegistrations(chains);

                ParseResult? result = null;
                var ex = Record.Exception(() => result = CommandResolver.Resolve(args, registrations));

                Assert.Null(ex);
                Assert.NotNull(result);
            },
            iter: ITERATIONS,
            seed: "0000ResolverNoThrw");
    }

    [Fact]
    public void Resolve_StructuralStrings_NeverThrows()
    {
        Gen.Select(RegistrationSpecGen, StructuralInput.Array[0, 8]).Sample(pair =>
            {
                var (chains, args) = pair;
                var registrations = BuildRegistrations(chains);

                ParseResult? result = null;
                var ex = Record.Exception(() => result = CommandResolver.Resolve(args, registrations));

                Assert.Null(ex);
                Assert.NotNull(result);
            },
            iter: ITERATIONS,
            seed: "0000ResolverStruct");
    }

    [Fact]
    public void Resolve_SuccessfulChain_ReconstructsFromVocabulary()
    {
        long successes = 0;

        Gen.Select(RegistrationSpecGen, TokenStrings).Sample(pair =>
            {
                var (chains, args) = pair;
                var registrations = BuildRegistrations(chains);

                var result = CommandResolver.Resolve(args, registrations);
                if (!result.Success || result.Chain is null)
                {
                    return;
                }

                Interlocked.Increment(ref successes);

                var inputValues = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);

                foreach (var node in result.Chain.Nodes)
                {
                    Assert.True(MatchesVocabulary(node.Descriptor, node.MatchedName));

                    foreach (var target in node.TargetValues)
                    {
                        foreach (var piece in target.Value.Split(','))
                        {
                            Assert.Contains(piece, inputValues);
                        }
                    }
                }
            },
            iter: ITERATIONS,
            seed: "0000ResolverReconst");

        Assert.True(Volatile.Read(ref successes) > 0);
    }
}
