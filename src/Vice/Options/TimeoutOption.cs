namespace Vice.Options;

[ViceOption]
public sealed record TimeoutOption()
    : RangeBearingOption("timeout",
                         "Maximum I/O wait time in milliseconds",
                         static ms => ms >= 0 && ms <= 30000,
                         "5000");
