using Vice.Logging;
using Vice.Research;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResearchSourceResolutionTests
{
    [Fact]
    public void Resolve_ByPrimaryName_ReturnsSource()
    {
        var source = ResearchSources.Resolve("arxiv");

        Assert.Equal("arxiv", source.Name);
    }

    [Fact]
    public void Resolve_ByAlias_ReturnsSameSource()
    {
        var byName = ResearchSources.Resolve("pubmed");
        var byAlias = ResearchSources.Resolve("pmc");

        Assert.Same(byName, byAlias);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var source = ResearchSources.Resolve("ArXiV");

        Assert.Equal("arxiv", source.Name);
    }

    [Fact]
    public void Resolve_UnknownSource_ThrowsBadArgument()
    {
        var ex = Assert.Throws<BadArgument>(() => ResearchSources.Resolve("nonexistent"));
        Assert.Contains("Unknown research source", ex.Detail);
        Assert.Contains("arxiv", ex.Detail);
    }
}

public sealed class ResearchDownloadPathsTests
{
    [Fact]
    public void BuildDestinationPath_NullToPath_UsesCurrentDirectoryAndSanitizesId()
    {
        var path = ResearchDownloadPaths.BuildDestinationPath(null, "arxiv", "10.1000/abc:def", "pdf");

        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "10.1000_abc_def.pdf"), path);
    }

    [Fact]
    public void BuildDestinationPath_WhitespaceToPath_UsesCurrentDirectory()
    {
        var path = ResearchDownloadPaths.BuildDestinationPath("   ", "arxiv", "id1", "txt");

        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "id1.txt"), path);
    }

    [Fact]
    public void BuildDestinationPath_ExistingDirectory_AppendsFile()
    {
        using var dir = new TempDir();

        var path = ResearchDownloadPaths.BuildDestinationPath(dir.Path, "arxiv", "id2", "xml");

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "id2.xml"), path);
    }

    [Fact]
    public void BuildDestinationPath_TrailingSeparator_TreatedAsDirectory()
    {
        var toPath = "/tmp/does-not-exist-dir" + Path.DirectorySeparatorChar;

        var path = ResearchDownloadPaths.BuildDestinationPath(toPath, "arxiv", "id3", "fasta");

        Assert.Equal(Path.Combine(Path.GetFullPath(toPath), "id3.fasta"), path);
    }

    [Fact]
    public void BuildDestinationPath_ExplicitFilePath_UsedVerbatim()
    {
        var toPath = Path.Combine(Path.GetTempPath(), $"vice-explicit-{Guid.NewGuid():N}.pdb");

        var path = ResearchDownloadPaths.BuildDestinationPath(toPath, "alphafold", "id4", "pdb");

        Assert.Equal(Path.GetFullPath(toPath), path);
    }

    [Fact]
    public void BuildUrlDestinationPath_NullToPath_UsesCurrentDirectoryWithSanitizedName()
    {
        var path = ResearchDownloadPaths.BuildUrlDestinationPath(null, "weird name?.bin");

        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "weird_name_.bin"), path);
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vice-net-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }
}
