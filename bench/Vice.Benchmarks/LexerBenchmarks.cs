using BenchmarkDotNet.Attributes;
using Vice.Parser;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class LexerBenchmarks
{
    private static readonly string[] ArgsForm =
    {
        "download",
        "arxiv",
        "2401.00001",
        "--to=/tmp/out.pdf",
        "--format=pdf",
        "alpha,beta,gamma",
    };

    private const string StringForm =
        "download arxiv 2401.00001 --to=/tmp/out.pdf --format=pdf alpha,beta,gamma";

    [Benchmark(Baseline = true)]
    public IReadOnlyList<Token> TokenizeArgs()
    {
        return Lexer.Tokenize(ArgsForm);
    }

    [Benchmark]
    public IReadOnlyList<Token> TokenizeString()
    {
        return Lexer.Tokenize(StringForm);
    }
}
