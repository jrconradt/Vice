namespace Vice.Jobs;

public sealed class NonRetryableJobException : Exception
{
    public NonRetryableJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
