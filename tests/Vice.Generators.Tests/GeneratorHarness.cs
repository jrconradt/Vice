using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Vice.Generators;
using Vice.Host;

namespace Vice.Generators.Tests;

internal static class GeneratorHarness
{
    public static GeneratorRunResult Run(string userSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(userSource, new CSharpParseOptions(LanguageVersion.Latest));

        var references = new List<MetadataReference>();
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "netstandard.dll", "System.Collections.dll", "System.Linq.dll", "System.Threading.Tasks.dll", "System.Console.dll", "System.ObjectModel.dll" })
        {
            var path = Path.Combine(coreDir, dll);
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
        references.Add(MetadataReference.CreateFromFile(typeof(Vice.Core.IViceApp).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Vice.Composition.ViceHostAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Vice.Host.ViceAppBuilder).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ViceCompositionGenerator(), new ViceCommandTargetsGenerator());
        var updated = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var inputDiagnostics);
        var result = updated.GetRunResult();

        var generatorDiagnostics = result.Diagnostics;
        var outputDiagnostics = outputCompilation.GetDiagnostics();

        var generatedTrees = outputCompilation.SyntaxTrees.Where(t => t != syntaxTree).ToImmutableArray();
        var generatedSources = generatedTrees.Select(t => t.ToString()).ToImmutableArray();

        return new GeneratorRunResult(generatedSources, generatorDiagnostics, outputDiagnostics, inputDiagnostics);
    }
}

internal sealed record GeneratorRunResult(
    ImmutableArray<string> GeneratedSources,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics,
    ImmutableArray<Diagnostic> InputDiagnostics)
{
    public string CombinedSource => string.Join("\n----\n", GeneratedSources);
    public IEnumerable<Diagnostic> AllDiagnostics => GeneratorDiagnostics.Concat(CompilationDiagnostics);
}
