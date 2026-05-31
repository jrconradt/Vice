namespace Vice.Parser;

public static class Lexer
{
    public static IReadOnlyList<Token> Tokenize(string[] args)
    {
        var tokens = new List<Token>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                EmitOption(tokens, arg[2..], i);
            }
            else if (arg.Length >= 2 && arg[0] == '-'
                     && char.IsAsciiLetter(arg[1]))
            {
                EmitOption(tokens, arg[1..], i);
            }
            else if (arg.StartsWith("args[", StringComparison.Ordinal) && arg.EndsWith(']'))
            {
                tokens.Add(new Token(TokenKind.ArgReference, arg, i));
            }
            else if (arg.Length >= 2
                     && ((arg[0] == '"' && arg[^1] == '"')
                         || (arg[0] == '\'' && arg[^1] == '\'')))
            {
                tokens.Add(new Token(TokenKind.Quoted, arg[1..^1], i));
            }
            else if (arg.Contains(','))
            {

                var parts = arg.Split(',');
                for (int j = 0; j < parts.Length; j++)
                {
                    if (j > 0)
                    {
                        tokens.Add(new Token(TokenKind.CommaSeparator, ",", i));
                    }

                    if (!string.IsNullOrEmpty(parts[j]))
                    {
                        tokens.Add(new Token(TokenKind.Word, parts[j], i));
                    }
                }
            }
            else
            {
                tokens.Add(new Token(TokenKind.Word, arg, i));
            }
        }
        return tokens;
    }

    public static IReadOnlyList<Token> Tokenize(string input)
    {
        var args = SplitArgs(input);
        return Tokenize(args);
    }

    private static void EmitOption(List<Token> tokens, string body, int position)
    {
        var eq = body.IndexOf('=');
        if (eq < 0)
        {
            tokens.Add(new Token(TokenKind.GlobalOption, body, position));
            return;
        }
        tokens.Add(new Token(TokenKind.GlobalOption, body[..eq], position));
        if (eq + 1 < body.Length)
        {
            tokens.Add(new Token(TokenKind.Word, body[(eq + 1)..], position));
        }
    }

    private static string[] SplitArgs(string input)
    {
        var args = new List<string>();
        var current = new List<char>();
        bool inQuote = false;
        char quoteChar = '\0';

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (inQuote)
            {
                current.Add(c);
                if (c == quoteChar)
                {
                    inQuote = false;
                }
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                current.Add(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Count > 0)
                {
                    args.Add(new string(current.ToArray()));
                    current.Clear();
                }
            }
            else
            {
                current.Add(c);
            }
        }

        if (current.Count > 0)
        {
            args.Add(new string(current.ToArray()));
        }

        return args.ToArray();
    }
}
