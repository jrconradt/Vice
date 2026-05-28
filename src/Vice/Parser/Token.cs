namespace Vice.Parser;

public sealed class Token
{
    public TokenKind Kind { get; }
    public string Value { get; }
    public int Position { get; }

    public Token(TokenKind kind, string value, int position)
    {
        Kind = kind;
        Value = value;
        Position = position;
    }

    public override string ToString() => Kind switch
    {
        TokenKind.Quoted => $"\"{Value}\"",
        TokenKind.GlobalOption => $"--{Value}",
        _ => Value
    };
}
