namespace Vice.Parser;

public record PipelineSegment(
    CommandChain Chain,
    int MatchedRegistrationIndex,
    string? OperatorWord);
