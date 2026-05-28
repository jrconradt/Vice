namespace Vice.Options;

public record FlagOption(string Name, string Description)
    : GlobalOption(Name, Description);
