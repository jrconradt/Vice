using Vice.Nodes;
using Vice.Parser;

namespace Vice.Lexicon;

public static class Connectors
{
    public static ConjunctiveNode To() => new("to");
    public static ConjunctiveNode With() => new("with");
    public static ConjunctiveNode For() => new("for");
    public static ConjunctiveNode From() => new("from");
    public static ConjunctiveNode In() => new("in");
    public static ConjunctiveNode Into() => new("into");
    public static ConjunctiveNode As() => new("as");
    public static ConjunctiveNode On() => new("on");
    public static ConjunctiveNode By() => new("by");
    public static ConjunctiveNode And() => new("and");

    public static ConjunctiveNode Then() => new("then", ConjunctiveKind.StageSeparator);
    public static ConjunctiveNode Send() => new("send", ConjunctiveKind.StageSeparator);
    public static ConjunctiveNode AndPipe() => new("and", ConjunctiveKind.StageSeparator);
    public static ConjunctiveNode Pipe() => new("pipe", ConjunctiveKind.StageSeparator);
    public static ConjunctiveNode Or() => new("or", ConjunctiveKind.StageSeparator);
}
