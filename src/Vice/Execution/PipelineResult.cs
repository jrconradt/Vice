namespace Vice.Execution;

internal sealed class PipelineResult
{
    public int ExitCode { get; }
    public string Output { get; }

    public PipelineResult(int exitCode, string output)
    {
        ExitCode = exitCode;
        Output = output;
    }
}
