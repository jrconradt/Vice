using System.Reflection;
using Vice.Commands;
using Xunit;

namespace Vice.Tests.Plugins;

public class MultiProcessPipelineTests
{
    [Fact]
    public async Task SanityShellPipeline_TransfersBytesUnchanged()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var tmpIn = Path.Combine(Path.GetTempPath(), $"vice-mpp-in-{Guid.NewGuid():N}.bin");
        var tmpOut = Path.Combine(Path.GetTempPath(), $"vice-mpp-out-{Guid.NewGuid():N}.bin");
        try
        {
            var data = new byte[16384];
            new Random(42).NextBytes(data);
            await File.WriteAllBytesAsync(tmpIn, data);

            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"cat \"{tmpIn}\" | cat > \"{tmpOut}\"");

            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            Assert.Equal(0, p.ExitCode);
            Assert.Equal(data, await File.ReadAllBytesAsync(tmpOut));
        }
        finally
        {
            if (File.Exists(tmpIn))
            {
                File.Delete(tmpIn);
            }

            if (File.Exists(tmpOut))
            {
                File.Delete(tmpOut);
            }
        }
    }

    [Fact]
    public async Task MultiProcessPipeline_RejectsAllRegistrySegments()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var asm = typeof(Vice.ViceApp).Assembly;
        var mppType = asm.GetType("Vice.Plugins.MultiProcessPipeline")!;
        var splitterType = asm.GetType("Vice.Plugins.RawArgsSplitter")!;

        var segObj = splitterType.GetMethod("Split")!.Invoke(null, new object[] { new[] { "nope", "then", "alsonope" } })!;

        var registry = new CommandRegistry();
        var runMethod = mppType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;
        var result = (Task<int>)runMethod.Invoke(null, new object?[]
        {
            "vice", segObj, registry, CancellationToken.None
        })!;
        var exit = await result;
        Assert.Equal(127, exit);
    }
}
