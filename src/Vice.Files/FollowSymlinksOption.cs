using Vice.Options;

namespace Vice.Files;

[ViceOption]
public sealed record FollowSymlinksOption()
    : FlagOption("follow-symlinks", "search: descend into symlinked directories during the walk");
