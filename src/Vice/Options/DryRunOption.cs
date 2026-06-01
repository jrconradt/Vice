namespace Vice.Options;

[ViceOption]
public sealed record DryRunOption()
    : FlagOption("dry-run", "Show what would happen without making changes");
