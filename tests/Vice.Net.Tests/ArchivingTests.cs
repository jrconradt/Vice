using System.IO.Compression;
using System.Text;
using Vice.Network.gRPC;
using Vice.Network.gRPC.Compression;
using Xunit;

namespace Vice.Net.Tests;

public class ArchivingTests
{
    private static string MakeScratchDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vice-arch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }

    private static string BuildZip(string scratch, Action<ZipArchive> populate, string name = "test.zip")
    {
        var zipPath = Path.Combine(scratch, name);
        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            populate(archive);
        }
        return zipPath;
    }

    private static void WriteText(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public async Task ExtractAsync_HappyPath_TwoFiles_WritesContents()
    {
        var scratch = MakeScratchDir();
        try
        {
            var zipPath = BuildZip(scratch, archive =>
            {
                WriteText(archive, "a.txt", "alpha");
                WriteText(archive, "sub/b.txt", "bravo");
            });

            var dest = Path.Combine(scratch, "out");
            await Archiving.ExtractAsync(zipPath, dest, CancellationToken.None);

            Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(dest, "a.txt")));
            Assert.Equal("bravo", await File.ReadAllTextAsync(Path.Combine(dest, "sub", "b.txt")));
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_ZipSlip_RelativeEscape_Throws_AndWritesNothingOutside()
    {
        var scratch = MakeScratchDir();
        var sentinel = Path.Combine(scratch, "escape.txt");

        try
        {
            var zipPath = BuildZip(scratch, archive =>
            {
                WriteText(archive, "../escape.txt", "PWNED");
            });

            var dest = Path.Combine(scratch, "out");
            Directory.CreateDirectory(dest);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));

            Assert.False(File.Exists(sentinel),
                $"zip-slip wrote outside dest: {sentinel}");
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_ZipSlip_AbsoluteRoot_Throws()
    {
        var scratch = MakeScratchDir();
        try
        {
            var absoluteEntry = OperatingSystem.IsWindows()
                ? "C:\\evil.txt"
                : "/tmp/vice-evil-should-never-write.txt";

            var zipPath = BuildZip(scratch, archive =>
            {
                WriteText(archive, absoluteEntry, "PWNED");
            });

            var dest = Path.Combine(scratch, "out");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));

            if (!OperatingSystem.IsWindows())
            {
                Assert.False(File.Exists("/tmp/vice-evil-should-never-write.txt"));
            }
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_PerEntryCapExceeded_Throws()
    {
        var scratch = MakeScratchDir();
        try
        {
            var oversized = Archiving.MaxPerEntryBytes + 1;

            var zipPath = Path.Combine(scratch, "big.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry("huge.bin", CompressionLevel.SmallestSize);
                using var s = entry.Open();
                var chunk = new byte[1 << 20];
                long written = 0;
                while (written < oversized)
                {
                    var toWrite = (int)Math.Min(chunk.Length, oversized - written);
                    s.Write(chunk, 0, toWrite);
                    written += toWrite;
                }
            }

            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));

            Assert.Contains("per-entry cap", ex.Message);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_TotalArchiveCapExceeded_Throws()
    {
        var scratch = MakeScratchDir();
        try
        {
            var perEntry = Archiving.MaxPerEntryBytes;
            var entriesNeeded = (int)(Archiving.MaxTotalExpandedBytes / perEntry) + 2;

            var zipPath = Path.Combine(scratch, "many.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var chunk = new byte[1 << 20];
                for (var i = 0; i < entriesNeeded; i++)
                {
                    var entry = archive.CreateEntry($"f{i}.bin", CompressionLevel.SmallestSize);
                    using var s = entry.Open();
                    long written = 0;
                    while (written < perEntry)
                    {
                        var toWrite = (int)Math.Min(chunk.Length, perEntry - written);
                        s.Write(chunk, 0, toWrite);
                        written += toWrite;
                    }
                }
            }

            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));

            Assert.Contains("total", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_RejectsTooManyEntries()
    {
        var scratch = MakeScratchDir();
        try
        {
            var zipPath = Path.Combine(scratch, "many.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                for (var i = 0; i <= Archiving.MaxEntries; i++)
                {
                    archive.CreateEntry($"f{i}.txt", CompressionLevel.NoCompression);
                }
            }

            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));

            Assert.Contains("entries", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_ZipSymlinkEntry_Refused_Linux()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var scratch = MakeScratchDir();
        try
        {
            var zipPath = Path.Combine(scratch, "symlink.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry("link", CompressionLevel.NoCompression);
                entry.ExternalAttributes = unchecked((int)((0xA000u | 0x1FFu) << 16));
                using var s = entry.Open();
                var target = Encoding.UTF8.GetBytes("/etc/passwd");
                s.Write(target, 0, target.Length);
            }

            uint roundTrippedAttr;
            using (var verifyFs = File.OpenRead(zipPath))
            using (var verifyArchive = new ZipArchive(verifyFs, ZipArchiveMode.Read))
            {
                var roundTripped = verifyArchive.Entries[0];
                roundTrippedAttr = (uint)roundTripped.ExternalAttributes;
            }
            var roundTrippedMode = (roundTrippedAttr >> 16) & 0xF000;
            if (roundTrippedMode != 0xA000)
            {
                return;
            }

            var dest = Path.Combine(scratch, "out");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => Archiving.ExtractAsync(zipPath, dest, CancellationToken.None));
            Assert.Contains("symlink", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task ExtractAsync_DestDirIsSymlink_StillContainsEntries()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var scratch = MakeScratchDir();
        try
        {
            var realDest = Path.Combine(scratch, "real-dest");
            Directory.CreateDirectory(realDest);
            var linkDest = Path.Combine(scratch, "link-dest");
            File.CreateSymbolicLink(linkDest, realDest);

            var zipPath = BuildZip(scratch, archive =>
            {
                WriteText(archive, "ok.txt", "ok");
            });

            var resolved = await Archiving.ExtractAsync(zipPath, linkDest, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(linkDest, "ok.txt")));
            Assert.True(File.Exists(Path.Combine(realDest, "ok.txt")));
            Assert.NotNull(resolved);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public void IsArchive_DetectsSupportedSuffixes()
    {
        Assert.True(Archiving.IsArchive("a.zip"));
        Assert.True(Archiving.IsArchive("a.tar"));
        Assert.True(Archiving.IsArchive("a.tar.gz"));
        Assert.True(Archiving.IsArchive("a.tgz"));
        Assert.False(Archiving.IsArchive("a.gz"));
        Assert.False(Archiving.IsArchive("a.txt"));
    }
}
