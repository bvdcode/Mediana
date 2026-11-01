// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Png
{
    internal class PngIdatWriteStream(Stream destination, Memory<byte> chunk, CancellationToken cancellationToken) : Stream
    {
        private int pending;
        private bool aborted;
        private bool disposed;
        public long WrittenBytes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed && !aborted;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        // Exceptional compressor cleanup may emit a trailer; partial output must remain untouched.
        public void Abort() => aborted = true;

        public async ValueTask CompleteAsync()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (pending != 0)
            {
                await WriteChunkAsync().ConfigureAwait(false);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (aborted)
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(buffer.Length, chunk.Length - 12 - pending);
                buffer.Span[..count].CopyTo(chunk.Span.Slice(8 + pending, count));
                pending += count;
                buffer = buffer[count..];
                if (pending == chunk.Length - 12)
                {
                    await WriteChunkAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask WriteChunkAsync()
        {
            "IDAT"u8.CopyTo(chunk.Span.Slice(4, 4));
            PngChunk.Finish(chunk.Span, pending, cancellationToken);
            int count = pending + 12;
            await destination.WriteAsync(chunk[..count], cancellationToken).ConfigureAwait(false);
            WrittenBytes = checked(WrittenBytes + count);
            pending = 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            base.Dispose(disposing);
        }
    }
}
