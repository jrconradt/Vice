using Xunit;

namespace Vice.Tests;

public sealed class EnvVarSerialFixture
{
}

[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection : ICollectionFixture<EnvVarSerialFixture>
{
}

internal sealed class EnvScope : IDisposable
{
    private readonly List<(string Name, string? Original)> _previous = new();

    public EnvScope(EnvVarSerialFixture serial,
                    params (string Name, string? Value)[] sets)
    {
        ArgumentNullException.ThrowIfNull(serial);
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
