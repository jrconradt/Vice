using Xunit;

namespace Vice.Tests;

internal sealed class EnvScope : IDisposable
{
    private readonly List<(string Name, string? Original)> _previous = new();

    public EnvScope(params (string Name, string? Value)[] sets)
    {
        foreach (var (name, value) in sets)
        {
            _previous.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in _previous)
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}

[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection { }
