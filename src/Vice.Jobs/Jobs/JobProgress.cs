namespace Vice.Jobs;

public record JobProgress(
    long? Current = null,
    long? Total = null,
    string? Label = null)
{
    public bool IsIndeterminate => Fraction is null;

    public double? Fraction =>
    Current is not null && Total is > 0
        ? Current.Value / (double)Total.Value
        : null;
}
