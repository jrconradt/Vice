using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vice.Files;
using Xunit;

namespace Vice.Files.Tests;

public class ArchiveSymlinkHardeningTests
{
    private static string NewScratchDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vice-archive-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BuildZip(string scratch, params (string Name, byte[] Body)[] entries)
    {
        var path = Path.Combine(scratch, $"archive-{Guid.NewGuid():N}.zip");
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(body, 0, body.Length);
            }
        }
        return path;
    }

    private static string BuildTar(string scratch, params (string Name, byte[] Body, TarEntryType Type)[] entries)
    {
        var path = Path.Combine(scratch, $"archive-{Guid.NewGuid():N}.tar");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new TarWriter(fs, TarEntryFormat.Pax, leaveOpen: false))
        {
            foreach (var (name, body, type) in entries)
            {
                var entry = new PaxTarEntry(type, name);
                if (body.Length > 0)
                {
                    entry.DataStream = new MemoryStream(body);
                }

                writer.WriteEntry(entry);
            }
        }
        return path;
    }

    private static bool TryCreateSymlink(string linkPath, string target, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, target);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task ExtractZip_HappyPath_ExtractsNestedFile()
    {
        var scratch = NewScratchDir("happy-zip");
        try
        {
            var body = Encoding.UTF8.GetBytes("hello");
            var zip = BuildZip(scratch, ("outer/inner.txt", body));
            var dest = Path.Combine(scratch, "out");

            var resolved = await Archiving.ExtractAsync(zip, dest, CancellationToken.None);

            var outFile = Path.Combine(resolved, "outer", "inner.txt");
            Assert.True(File.Exists(outFile));
            Assert.Equal("hello", await File.ReadAllTextAsync(outFile));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractZip_TraversalEntry_Rejected()
    {
        var scratch = NewScratchDir("zipslip-traversal");
        try
        {
            var body = Encoding.UTF8.GetBytes("pwn");
            var zip = BuildZip(scratch, ("../escape.txt", body));
            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zip, dest, CancellationToken.None));
            Assert.Contains("zip-slip", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [UnixOnlyFact]
    public async Task ExtractZip_SymlinkedDestSubdir_Rejected()
    {
        var scratch = NewScratchDir("zipslip-symlink");
        try
        {
            var dest = Path.Combine(scratch, "dest");
            var target = Path.Combine(scratch, "target");
            Directory.CreateDirectory(dest);
            Directory.CreateDirectory(target);

            var linkPath = Path.Combine(dest, "sub");
            Assert.True(TryCreateSymlink(linkPath, target, isDirectory: true));

            var body = Encoding.UTF8.GetBytes("payload");
            var zip = BuildZip(scratch, ("sub/payload.txt", body));

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zip, dest, CancellationToken.None));
            Assert.Contains("zip-slip", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(target, "payload.txt")));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractTar_TraversalEntry_Rejected()
    {
        var scratch = NewScratchDir("tar-traversal");
        try
        {
            var body = Encoding.UTF8.GetBytes("pwn");
            var tar = BuildTar(scratch, ("../escape.txt", body, TarEntryType.RegularFile));
            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(tar, dest, CancellationToken.None));
            Assert.Contains("zip-slip", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [UnixOnlyFact]
    public async Task ExtractTar_SymlinkedDestSubdir_Rejected()
    {
        var scratch = NewScratchDir("tar-symlink");
        try
        {
            var dest = Path.Combine(scratch, "dest");
            var target = Path.Combine(scratch, "target");
            Directory.CreateDirectory(dest);
            Directory.CreateDirectory(target);

            var linkPath = Path.Combine(dest, "sub");
            Assert.True(TryCreateSymlink(linkPath, target, isDirectory: true));

            var body = Encoding.UTF8.GetBytes("payload");
            var tar = BuildTar(scratch, ("sub/payload.txt", body, TarEntryType.RegularFile));

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(tar, dest, CancellationToken.None));
            Assert.Contains("zip-slip", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(target, "payload.txt")));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
