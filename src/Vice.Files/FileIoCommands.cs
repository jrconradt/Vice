using System.Text;
using Vice.Composition;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Persistence;
using Vice.Streaming;
using static Vice.Dsl;

namespace Vice.Files;

[ViceCommandPack]
public static class FileIoCommands
{
    private const int DefaultChunkSize = 81920;
    private const int MaxChunkSize = 16 * 1024 * 1024;

    public static void Register(IViceApp app)
    {
        app.RegisterStreaming<byte[]>(
            Verbs.Read() * Targets.Path,
            "Read a file; standalone decodes UTF-8 to stdout, piped emits raw byte chunks",
            ReadAsStream,
            classicFallback: ReadToConsole);

        app.RegisterStreamConsumer<byte[]>(
            Verbs.Write() > Connectors.To() > Nouns.File() * Targets.Path,
            "Overwrite a file with the upstream pipeline bytes",
            WriteToFile);

        app.RegisterStreamConsumer<byte[]>(
            Verbs.Append() > Connectors.To() > Nouns.File() * Targets.Path,
            "Append the upstream pipeline bytes to a file",
            AppendToFile);

        app.RegisterStreamConsumer<byte[]>(
            Verbs.Stream() > Connectors.To() > Nouns.Console(),
            "Write the upstream byte stream to stdout",
            StreamConsoleConsumer.HandleAsync);

        app.RegisterStreamConsumer<byte[]>(
            Verbs.Stream() > Connectors.To() > Nouns.File() * Targets.Path,
            "Overwrite a file with the upstream byte stream",
            WriteToFile);

        app.RegisterStreamConsumer<byte[]>(
            Verbs.Stream() > Connectors.To() > Nouns.Count(),
            "Discard the upstream byte stream; print chunk and byte counts",
            StreamCountConsumer.HandleAsync);

        app.Register(
            Verbs.Unarchive() * Targets.Path,
            "Extract a .zip/.tar/.tar.gz/.tgz archive to a temp directory",
            UnarchiveAsync);

        app.Register(
            Verbs.Unarchive() * Targets.Path > Connectors.To() > Nouns.Dir() * Targets.Dest,
            "Extract a .zip/.tar/.tar.gz/.tgz archive to a destination directory",
            UnarchiveAsync);
    }

    private static int ChunkSizeOf(ICommandContext ctx)
    {
        var chunkSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? DefaultChunkSize;
        if (chunkSize > MaxChunkSize)
        {
            throw new BadArgument($"chunk-size {chunkSize} exceeds the maximum of {MaxChunkSize} bytes.");
        }

        return chunkSize;
    }

    private static async Task<int> ReadAsStream(
        IStreamingCommandContext<byte[]> ctx,
        CancellationToken ct)
    {
        var resolved = Path.GetFullPath(ctx.Require("path"));
        var chunkSize = ChunkSizeOf(ctx);

        try
        {
            await using var source = Decompression.OpenReadStream(resolved);
            var buffer = new byte[chunkSize];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false)) > 0)
            {
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await ctx.Stream.YieldAsync(chunk, ct).ConfigureAwait(false);
            }

            ctx.Stream.Complete();
            return ViceExitCode.SUCCESS;
        }
        catch (Exception ex)
        {
            ctx.Stream.Fault(ex);
            throw;
        }
    }

    private static async Task<int> ReadToConsole(CommandContext ctx, CancellationToken ct)
    {
        var resolved = Path.GetFullPath(ctx.Require("path"));
        var chunkSize = ChunkSizeOf(ctx);

        await using var source = Decompression.OpenReadStream(resolved);
        await using var stdout = System.Console.OpenStandardOutput();
        var decoder = Encoding.UTF8.GetDecoder();
        var buffer = new byte[chunkSize];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(chunkSize)];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false)) > 0)
        {
            var charCount = decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
            if (charCount > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(chars, 0, charCount);
                await stdout.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
            }
        }

        var tail = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        if (tail > 0)
        {
            var bytes = Encoding.UTF8.GetBytes(chars, 0, tail);
            await stdout.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
        }

        await stdout.FlushAsync(ct).ConfigureAwait(false);
        return ViceExitCode.SUCCESS;
    }

    private static Task<int> WriteToFile(
        IConsumingCommandContext<byte[]> ctx,
        CancellationToken ct)
        => WriteStreamAsync(ctx,
                            FileMode.Create,
                            FileShare.None,
                            ct);

    private static Task<int> AppendToFile(
        IConsumingCommandContext<byte[]> ctx,
        CancellationToken ct)
        => WriteStreamAsync(ctx,
                            FileMode.Append,
                            FileShare.Read,
                            ct);

    private static async Task<int> WriteStreamAsync(
        IConsumingCommandContext<byte[]> ctx,
        FileMode mode,
        FileShare share,
        CancellationToken ct)
    {
        var resolved = Path.GetFullPath(ctx.Require("path"));
        if (!SafeWriteRoots.IsAllowed(resolved, out var reason))
        {
            throw new BadArgument($"Destination '{resolved}' is outside allowed write roots: {reason}.");
        }

        var dir = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (mode == FileMode.Append)
        {
            await using var dest = new FileStream(
                resolved,
                mode,
                FileAccess.Write,
                share,
                bufferSize: DefaultChunkSize,
                useAsync: true);
            await StreamLoop.RunAsync(
                ctx.Input,
                async chunk =>
                {
                    await dest.WriteAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
                },
                ct);
            await dest.FlushAsync(ct).ConfigureAwait(false);
            return ViceExitCode.SUCCESS;
        }

        var partial = resolved + ".partial";
        try
        {
            await using (var dest = new FileStream(
                partial,
                FileMode.Create,
                FileAccess.Write,
                share,
                bufferSize: DefaultChunkSize,
                useAsync: true))
            {
                await StreamLoop.RunAsync(
                    ctx.Input,
                    async chunk =>
                    {
                        await dest.WriteAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
                    },
                    ct);
                await dest.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(partial, resolved, overwrite: true);
            return ViceExitCode.SUCCESS;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<int> UnarchiveAsync(CommandContext ctx, CancellationToken ct)
    {
        var resolvedArchive = Path.GetFullPath(ctx.Require("path"));

        if (!Archiving.IsArchive(resolvedArchive))
        {
            Vice.Output.Error($"Not a supported archive: '{resolvedArchive}'. Supported: .zip, .tar, .tar.gz, .tgz");
            return ViceExitCode.USAGE_ERROR;
        }

        var dest = ctx["dest"];
        string? resolvedDest = null;
        if (dest is not null)
        {
            resolvedDest = Path.GetFullPath(dest);
            if (!SafeWriteRoots.IsAllowed(resolvedDest, out var reason))
            {
                Vice.Output.Error($"Destination '{dest}' refused: {reason}.");
                return ViceExitCode.USAGE_ERROR;
            }
        }

        var root = await Archiving.ExtractAsync(resolvedArchive, resolvedDest, ctx.Logger, ct).ConfigureAwait(false);
        Vice.Output.Line(root);
        return ViceExitCode.SUCCESS;
    }
}
