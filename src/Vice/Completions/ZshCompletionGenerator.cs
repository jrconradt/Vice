namespace Vice.Completions;

internal static class ZshCompletionGenerator
{
    public static string Generate(CompletionTrie trie)
    {
        var funcName = "_" + SanitizeIdentifier(trie.AppName);

        var globalsLines = new List<string>();
        foreach (var opt in trie.GlobalOptions)
        {
            var valueSuffix = opt.ValueBearing ? "=" : "";
            globalsLines.Add($"            '--{ZshEscape(opt.Name)}{valueSuffix}[{ZshEscape(opt.Description)}]'");
            foreach (var a in opt.Aliases)
            {
                var prefix = a.Length == 1 ? "-" : "--";
                globalsLines.Add($"            '{prefix}{ZshEscape(a)}{valueSuffix}[{ZshEscape(opt.Description)}]'");
            }
        }

        var (walkedTransitions, walkedSuggestions) = CompletionWalker.Walk(trie.Root);

        var transitionLines = new List<string>(walkedTransitions.Count);
        foreach (var t in walkedTransitions)
        {
            var parent = ZshEscape(t.ParentStateId);
            var child = ZshEscape(t.ChildStateId);
            var pattern = string.Join("|", t.Tokens.Select(tok => $"\"{parent}:{ZshEscape(tok)}\""));
            var setSkip = t.TargetCount > 0 ? $" skip={t.TargetCount};" : "";
            transitionLines.Add($"            {pattern}) state=\"{child}\";{setSkip} ;;");
        }

        var suggestionLines = new List<string>();
        foreach (var s in walkedSuggestions)
        {
            suggestionLines.Add($"        \"{ZshEscape(s.StateId)}\")");
            suggestionLines.Add("            local -a candidates");
            suggestionLines.Add("            candidates=(");
            foreach (var c in s.Candidates)
            {
                var primary = ZshEscape(c.Token);
                var desc = c.Description is null ? primary : ZshEscape(c.Description);
                suggestionLines.Add($"                '{primary}:{desc}'");
                foreach (var syn in c.Synonyms)
                {
                    suggestionLines.Add($"                '{ZshEscape(syn)}:{desc}'");
                }
            }
            suggestionLines.Add("            )");
            var depth = s.StateId == "root" ? "verb" : "subcommand";
            suggestionLines.Add($"            _describe -t {depth} '{depth}' candidates");
            suggestionLines.Add("            ;;");
        }

        var globalsBlock = string.Join(Environment.NewLine, globalsLines);
        var transitions = string.Join(Environment.NewLine, transitionLines);
        var suggestions = string.Join(Environment.NewLine, suggestionLines);

        return $$"""
            #compdef {{trie.AppName}}
            # zsh completion for {{trie.AppName}}
            # install: {{trie.AppName}} completions zsh > "${fpath[1]}/_{{trie.AppName}}"
            # or for the current shell: source <({{trie.AppName}} completions zsh)

            {{funcName}}() {
                local cur="${words[CURRENT]}"

                if [[ "$cur" == --* ]]; then
                    local -a globals
                    globals=(
            {{globalsBlock}}
                    )
                    _describe -t globals 'option' globals
                    return
                fi

                local state="root"
                local skip=0
                local i=2
                while (( i < CURRENT )); do
                    local word="${words[i]}"
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
                    _files
                    return
                fi

                case "$state" in
            {{suggestions}}
                    *) _files ;;
                esac
            }

            compdef {{funcName}} {{trie.AppName}}

            """.ReplaceLineEndings();
    }

    private static string ZshEscape(string s)
        => s.Replace("\\", "\\\\").Replace("'", "'\\''").Replace("[", "\\[").Replace("]", "\\]").Replace(":", "\\:");

    private static string SanitizeIdentifier(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
