using Vice.Composition;
using Vice.Options;

namespace Vice.Net.Options;

[ViceOption]
public sealed record NoReplyOption()
    : FlagOption("no-reply", "UDP: don't wait for a response");
