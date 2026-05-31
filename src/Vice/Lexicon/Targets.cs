namespace Vice.Lexicon;

public static class Targets
{
    public static readonly TargetDef Command = new("command", Required: false);
    public static readonly TargetDef Shell = new("shell");
    public static readonly TargetDef Data = new("data");
    public static readonly TargetDef DataOptional = new("data", Required: false);
    public static readonly TargetDef Path = new("path");
    public static readonly TargetDef PathOptional = new("path", Required: false);
    public static readonly TargetDef Endpoint = new("endpoint");
    public static readonly TargetDef Source = new("source");
    public static readonly TargetDef Id = new("id");
    public static readonly TargetDef Query = new("query");
    public static readonly TargetDef Method = new("method");
    public static readonly TargetDef Service = new("service");
    public static readonly TargetDef Axis = new("axis");
    public static readonly TargetDef Pattern = new("pattern");
    public static readonly TargetDef Root = new("root");
    public static readonly TargetDef Dest = new("dest");
    public static readonly TargetDef Sinks = new("sinks", Required: true, Variadic: true);
    public static readonly TargetDef Key = new("key");
    public static readonly TargetDef Value = new("value");
    public static readonly TargetDef JobId = new("job-id");
}
