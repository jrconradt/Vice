using Vice.Commands;
using Vice.Completions;
using Vice.Contracts;
using Vice.Display;
using Vice.Nodes;
using Vice.Options;
using Vice.TestSupport;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class CompletionsTests
{
    private static (ViceApp App, RecordingConsole Console) Build(string name = "vice")
    {
        var c = new RecordingConsole();
        var app = new ViceApp(name, "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("alpha", "a"), "alpha description", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("beta") > noun("things") * target("path"), "beta description", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("gamma"), "gamma description", (ctx, ct) => Task.FromResult(0), showInHelp: false);
        return (app, c);
    }

    [Fact]
    public async Task Bash_EmitsCompletionScriptForVice()
    {
        var (app, console) = Build();

        var exit = await app.RunAsync(new[] { "completions", "bash" });

        Assert.Equal(0, exit);
        Assert.Contains("# bash completion for vice", console.Output);
        Assert.Contains("_vice()", console.Output);
        Assert.Contains("complete -F _vice vice", console.Output);
        Assert.Contains("alpha", console.Output);
        Assert.Contains(" a ", console.Output);
        Assert.Contains("beta", console.Output);
    }

    [Fact]
    public async Task Bash_IncludesHiddenAndBuiltinVerbs()
    {
        var (app, console) = Build();

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("gamma", console.Output);
        Assert.Contains("help", console.Output);
        Assert.Contains("version", console.Output);
        Assert.Contains("completions", console.Output);
        Assert.Contains("daemon", console.Output);
    }

    [Fact]
    public async Task Bash_SanitizesDashedAppNameInFunctionIdentifier()
    {
        var (app, console) = Build("chain-asm");

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("_chain_asm()", console.Output);
        Assert.Contains("complete -F _chain_asm chain-asm", console.Output);
    }

    [Fact]
    public async Task Bash_EmitsGlobalOptions()
    {
        var (app, console) = Build();

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("--help", console.Output);
        Assert.Contains("--no-color", console.Output);
    }

    [Fact]
    public async Task Zsh_EmitsCompletionScriptForVice()
    {
        var (app, console) = Build();

        var exit = await app.RunAsync(new[] { "completions", "zsh" });

        Assert.Equal(0, exit);
        Assert.Contains("#compdef vice", console.Output);
        Assert.Contains("_vice()", console.Output);
        Assert.Contains("compdef _vice vice", console.Output);
        Assert.Contains("'alpha:alpha description'", console.Output);
        Assert.Contains("'a:alpha description'", console.Output);
        Assert.Contains("'beta:beta description'", console.Output);
    }

    [Fact]
    public async Task Zsh_EmitsGlobalOptionsWithDescriptions()
    {
        var (app, console) = Build();

        await app.RunAsync(new[] { "completions", "zsh" });

        Assert.Contains("'--help[", console.Output);
        Assert.Contains("'--no-color[", console.Output);
    }

    [Fact]
    public async Task Zsh_SanitizesDashedAppNameInFunctionIdentifier()
    {
        var (app, console) = Build("chain-asm");

        await app.RunAsync(new[] { "completions", "zsh" });

        Assert.Contains("#compdef chain-asm", console.Output);
        Assert.Contains("_chain_asm()", console.Output);
        Assert.Contains("compdef _chain_asm chain-asm", console.Output);
    }

    [Fact]
    public async Task Bash_EmitsTrieStateMachineForDeeperPositions()
    {
        var (app, console) = Build();
        app.Register(verb("inspect") > noun("assembly") * target("path"), "inspect assembly", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("inspect") > noun("info") * target("path"), "inspect info", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("\"root:inspect\") state=\"root__inspect\"", console.Output);
        Assert.Contains("\"root__inspect:assembly\") state=\"root__inspect__assembly\"; skip=1;", console.Output);
        Assert.Contains("\"root__inspect:info\") state=\"root__inspect__info\"; skip=1;", console.Output);
        Assert.Contains("\"root__inspect\") COMPREPLY=( $(compgen -W \"assembly info\"", console.Output);
    }

    [Fact]
    public async Task Bash_SynonymsMapToSameStateAtAnyDepth()
    {
        var (app, console) = Build();
        app.Register(verb("foo") > (noun("bar") | noun("baz")), "foo bar/baz", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("\"root__foo:bar\"|\"root__foo:baz\") state=\"root__foo__bar\"", console.Output);
    }

    [Fact]
    public async Task Bash_FallsBackToFileCompletionWhenStateUnknown()
    {
        var (app, console) = Build();

        await app.RunAsync(new[] { "completions", "bash" });

        Assert.Contains("*) COMPREPLY=( $(compgen -f -- \"$cur\") )", console.Output);
        Assert.Contains("if (( skip > 0 )); then", console.Output);
        Assert.Contains("compgen -f -- \"$cur\"", console.Output);
    }

    [Fact]
    public async Task Zsh_EmitsTrieStateMachineForDeeperPositions()
    {
        var (app, console) = Build();
        app.Register(verb("inspect") > noun("assembly") * target("path"), "inspect assembly", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("inspect") > noun("info") * target("path"), "inspect info", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "completions", "zsh" });

        Assert.Contains("\"root:inspect\") state=\"root__inspect\"", console.Output);
        Assert.Contains("\"root__inspect:assembly\") state=\"root__inspect__assembly\"; skip=1;", console.Output);
        Assert.Contains("\"root__inspect\")", console.Output);
        Assert.Contains("_describe -t subcommand 'subcommand' candidates", console.Output);
    }

    [Fact]
    public async Task UnsupportedShell_ReturnsErrorAndExitOne()
    {
        var (app, console) = Build();

        var exit = await app.RunAsync(new[] { "completions", "powershell" });

        Assert.Equal(2, exit);
        Assert.Contains("Unsupported shell", console.Error);
    }

    [Fact]
    public async Task Bash_FullScriptMatchesGolden()
    {
        var (app, console) = Build();

        var exit = await app.RunAsync(new[] { "completions", "bash" });

        Assert.Equal(0, exit);
        GoldenFile.Verify("completions_bash.golden", console.Output);
    }

    [Fact]
    public async Task Zsh_FullScriptMatchesGolden()
    {
        var (app, console) = Build();

        var exit = await app.RunAsync(new[] { "completions", "zsh" });

        Assert.Equal(0, exit);
        GoldenFile.Verify("completions_zsh.golden", console.Output);
    }

    private static CompletionTrie BuildTrie(params ChainNode[] chains)
    {
        var registrations = new List<CommandRegistration>();
        foreach (var chain in chains)
        {
            registrations.Add(new CommandRegistration(chain, "desc", (ctx, ct) => Task.FromResult(0)));
        }

        return CompletionModelBuilder.Build("app", registrations, System.Array.Empty<GlobalOption>());
    }

    private static CompletionNode Child(CompletionNode node, string token)
    {
        Assert.True(node.Children.TryGetValue(token, out var child), $"missing child '{token}' under '{node.Token}'");
        return child!;
    }

    private static void AssertStructurallyEqual(CompletionNode a, CompletionNode b)
    {
        Assert.Equal(a.Token, b.Token);
        Assert.Equal(a.IsTerminal, b.IsTerminal);
        Assert.Equal(a.Children.Count, b.Children.Count);
        foreach (var (token, childA) in a.Children)
        {
            Assert.True(b.Children.TryGetValue(token, out var childB), $"missing matching child '{token}'");
            AssertStructurallyEqual(childA, childB!);
        }
    }

    [Fact]
    public void Trie_with_optional_tail_equals_two_separate_registrations()
    {
        var optionalTrie = BuildTrie(verb("foo") > optional(verb("bar")));
        var separateTrie = BuildTrie(verb("foo"), verb("foo") > verb("bar"));

        var foo1 = Child(optionalTrie.Root, "foo");
        var foo2 = Child(separateTrie.Root, "foo");
        Assert.True(foo1.IsTerminal);
        Assert.True(foo2.IsTerminal);
        Assert.True(Child(foo1, "bar").IsTerminal);
        Assert.True(Child(foo2, "bar").IsTerminal);

        AssertStructurallyEqual(optionalTrie.Root, separateTrie.Root);
    }

    [Fact]
    public void Trie_with_alternation_equals_separate_registrations_per_alternative()
    {
        var altTrie = BuildTrie(verb("foo") > oneOf(verb("a"), verb("b")));
        var separateTrie = BuildTrie(verb("foo") > verb("a"), verb("foo") > verb("b"));

        var foo = Child(altTrie.Root, "foo");
        Assert.True(Child(foo, "a").IsTerminal);
        Assert.True(Child(foo, "b").IsTerminal);

        AssertStructurallyEqual(altTrie.Root, separateTrie.Root);
    }

    [Fact]
    public void Trie_with_repetition_unrolls_to_default_depth()
    {
        var trie = BuildTrie(verb("foo") > repeat(verb("bar"), min: 1, max: int.MaxValue));

        var foo = Child(trie.Root, "foo");
        var bar1 = Child(foo, "bar");
        var bar2 = Child(bar1, "bar");
        var bar3 = Child(bar2, "bar");
        Assert.True(bar1.IsTerminal);
        Assert.True(bar2.IsTerminal);
        Assert.True(bar3.IsTerminal);
        Assert.False(bar3.Children.ContainsKey("bar"));
    }

    [Fact]
    public void Trie_with_repetition_respects_min_zero()
    {
        var trie = BuildTrie(verb("foo") > repeat(verb("bar"), min: 0, max: 3));

        var foo = Child(trie.Root, "foo");
        Assert.True(foo.IsTerminal);
        var bar1 = Child(foo, "bar");
        var bar2 = Child(bar1, "bar");
        var bar3 = Child(bar2, "bar");
        Assert.True(bar1.IsTerminal);
        Assert.True(bar2.IsTerminal);
        Assert.True(bar3.IsTerminal);
        Assert.False(bar3.Children.ContainsKey("bar"));
    }

    [Fact]
    public void Trie_with_repetition_and_separator_interleaves()
    {
        var trie = BuildTrie(verb("foo") > repeat(verb("bar"), min: 1, max: 3, separator: verb("and")));

        var foo = Child(trie.Root, "foo");
        var bar1 = Child(foo, "bar");
        Assert.True(bar1.IsTerminal);
        var and1 = Child(bar1, "and");
        var bar2 = Child(and1, "bar");
        Assert.True(bar2.IsTerminal);
        var and2 = Child(bar2, "and");
        var bar3 = Child(and2, "bar");
        Assert.True(bar3.IsTerminal);
    }
}
