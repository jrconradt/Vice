using Vice.Composition;

namespace Vice.Lexicon;

public static class Targets
{
    [TargetName("command")] public static readonly TargetDef Command = new("command", Required: false);
    [TargetName("shell")] public static readonly TargetDef Shell = new("shell");
    [TargetName("data")] public static readonly TargetDef Data = new("data");
    [TargetName("data")] public static readonly TargetDef DataOptional = new("data", Required: false);
    [TargetName("path")] public static readonly TargetDef Path = new("path");
    [TargetName("path")] public static readonly TargetDef PathOptional = new("path", Required: false);
    [TargetName("endpoint")] public static readonly TargetDef Endpoint = new("endpoint");
    [TargetName("source")] public static readonly TargetDef Source = new("source");
    [TargetName("id")] public static readonly TargetDef Id = new("id");
    [TargetName("query")] public static readonly TargetDef Query = new("query");
    [TargetName("method")] public static readonly TargetDef Method = new("method");
    [TargetName("service")] public static readonly TargetDef Service = new("service");
    [TargetName("axis")] public static readonly TargetDef Axis = new("axis");
    [TargetName("pattern")] public static readonly TargetDef Pattern = new("pattern");
    [TargetName("root")] public static readonly TargetDef Root = new("root");
    [TargetName("dest")] public static readonly TargetDef Dest = new("dest");
    [TargetName("n")] public static readonly TargetDef N = new("n");
    [TargetName("strategy")] public static readonly TargetDef Strategy = new("strategy");
    [TargetName("sinks")] public static readonly TargetDef Sinks = new("sinks", Required: true, Variadic: true);
    [TargetName("key")] public static readonly TargetDef Key = new("key");
    [TargetName("value")] public static readonly TargetDef Value = new("value");
    [TargetName("job-id")] public static readonly TargetDef JobId = new("job-id");
}
