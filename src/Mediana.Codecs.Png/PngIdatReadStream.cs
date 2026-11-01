// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Png
{
    /// <summary>Reads a validated consecutive IDAT region without concatenating its payloads.</summary>
    internal class PngIdatReadStream(ReadOnlyMemory<byte> region, CancellationToken token) : Stream
    {
        private int offset;
        private int remaining;
        private bool disposed;
        public override bool CanRead => !disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            if (buffer.IsEmpty)
            {
                return 0;
            }
            while (remaining == 0)
            {
                if (offset == region.Length)
                {
                    // A complete zlib stream stops reading before this boundary, including its Adler-32 trailer.
                    throw new InvalidDataException("PNG compressed data ended before the zlib stream completed.");
                }
                remaining = BinaryPrimitives.ReadInt32BigEndian(region.Span[offset..]);
                offset += 8;
                if (remaining == 0)
                {
                    offset += 4;
                }
            }
            int count = Math.Min(buffer.Length, Math.Min(remaining, 16 * 1024));
            region.Span.Slice(offset, count).CopyTo(buffer);
            offset += count;
            remaining -= count;
            if (remaining == 0)
            {
                offset += 4;
            }
            return count;
        }

        public override void Flush() => ObjectDisposedException.ThrowIf(disposed, this);
        public override long Seek(long value, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            base.Dispose(disposing);
        }
    }
}
