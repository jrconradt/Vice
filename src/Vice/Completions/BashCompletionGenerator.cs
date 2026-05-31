namespace Vice.Completions;

internal static class BashCompletionGenerator
{
    public static string Generate(CompletionTrie trie)
    {
        var funcName = "_" + SanitizeIdentifier(trie.AppName);
        var appName = BashEscape(trie.AppName);
        var globalTokens = string.Join(' ', trie.GlobalOptions
            .SelectMany(o =>
            {
                var list = new List<string> { "--" + o.Name };
                foreach (var a in o.Aliases)
                {
                    list.Add(a.Length == 1 ? "-" + a : "--" + a);
                }

                return list;
            })
            .Select(BashEscape));

        var (walkedTransitions, walkedSuggestions) = CompletionWalker.Walk(trie.Root);

        var transitionLines = new List<string>(walkedTransitions.Count);
        foreach (var t in walkedTransitions)
        {
            var parent = BashEscape(t.ParentStateId);
            var child = BashEscape(t.ChildStateId);
            var pattern = string.Join("|", t.Tokens.Select(tok => $"\"{parent}:{BashEscape(tok)}\""));
            var setSkip = t.TargetCount > 0 ? $" skip={t.TargetCount};" : "";
            transitionLines.Add($"            {pattern}) state=\"{child}\";{setSkip} ;;");
        }

        var suggestionLines = new List<string>(walkedSuggestions.Count);
        foreach (var s in walkedSuggestions)
        {
            var words = string.Join(' ', s.AllTokens.Select(BashEscape).Distinct());
            suggestionLines.Add($"        \"{BashEscape(s.StateId)}\") COMPREPLY=( $(compgen -W \"{words}\" -- \"$cur\") ) ;;");
        }

        var transitions = string.Join(Environment.NewLine, transitionLines);
        var suggestions = string.Join(Environment.NewLine, suggestionLines);

        return $$"""
            # bash completion for {{appName}}
            # install: {{appName}} completions bash > ~/.local/share/bash-completion/completions/{{appName}}
            # or for the current shell: source <({{appName}} completions bash)

            {{funcName}}() {
                local cur="${COMP_WORDS[COMP_CWORD]}"

                if [[ "$cur" == --* ]]; then
                    COMPREPLY=( $(compgen -W "{{globalTokens}}" -- "$cur") )
                    return 0
                fi

                local state="root"
                local skip=0
                local i=1
                while (( i < COMP_CWORD )); do
                    local word="${COMP_WORDS[$i]}"
                    if (( skip > 0 )); then
                        skip=$((skip-1))
                        i=$((i+1))
                        continue
                    fi
                    case "$state:$word" in
            {{transitions}}
                        *) state="unknown" ;;
                    esac
                    i=$((i+1))
                done

                if (( skip > 0 )); then
                    COMPREPLY=( $(compgen -f -- "$cur") )
                    return 0
                fi

                case "$state" in
            {{suggestions}}
                    *) COMPREPLY=( $(compgen -f -- "$cur") ) ;;
                esac
                return 0
            }

            complete -F {{funcName}} {{appName}}

            """.ReplaceLineEndings();
    }

    private static string BashEscape(string s)
        => string.Concat(s.Select(c => (c == '\\' || c == '"'
                                        || c == '$'
                                        || c == '`') ? $"\\{c}" : $"{c}"));

    private static string SanitizeIdentifier(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
