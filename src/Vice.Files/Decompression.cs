using System.IO.Compression;

namespace Vice.Files;

public static class Decompression
{
    public const long MAX_DECOMPRESSED_BYTES = 8L << 30;

    public static bool IsStreamCodec(string path)
        => ExtensionOf(path) is "gz" or "br" or "deflate";

    public static Stream WithDecompressionCap(Stream s, long cap)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        if (cap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cap), "cap must be positive.");
        }

        return new CountingReadStream(s, cap);
    }

    public static Stream OpenReadStream(string path, long? maxBytes = null)
    {
        var cap = maxBytes ?? MAX_DECOMPRESSED_BYTES;
        if (cap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);

        try
        {
            Stream inner = ExtensionOf(path) switch
            {
                "gz" => new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false),
                "br" => new BrotliStream(fs, CompressionMode.Decompress, leaveOpen: false),
                "deflate" => new DeflateStream(fs, CompressionMode.Decompress, leaveOpen: false),
                _ => fs
            };

            if (ReferenceEquals(inner, fs))
            {
                return fs;
            }

            return new CountingReadStream(inner, cap);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private static string ExtensionOf(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Length == 0)
        {
            return string.Empty;
        }

        return ext[1..].ToLowerInvariant();
    }

    private sealed class CountingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _read;

        public CountingReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n;
            try
            {
                n = _inner.Read(buffer, offset, count);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException("Compressed stream contained invalid data.", ex);
            }

            Account(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n;
            try
            {
                n = _inner.Read(buffer);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException("Compressed stream contained invalid data.", ex);
            }

            Account(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n;
            try
            {
                n = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException("Compressed stream contained invalid data.", ex);
            }

            Account(n);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();

        private void Account(int n)
        {
            if (n <= 0)
            {
                return;
            }

            _read += n;
            if (_read > _maxBytes)
            {
                throw new InvalidDataException(
                    $"Decompressed payload exceeded cap of {_maxBytes} bytes.");
            }
        }
    }
}
