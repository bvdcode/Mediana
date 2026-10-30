// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    /// <summary>A seekable read-only view over borrowed memory. The memory must remain valid until disposal.</summary>
    public class ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) : Stream
    {
        private ReadOnlyMemory<byte> data = memory;
        private long position;
        private bool disposed;

        public override bool CanRead => !disposed;
        public override bool CanSeek => !disposed;
        public override bool CanWrite => false;
        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return data.Length;
            }
        }
        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return position;
            }
            set
            {
                ThrowIfDisposed();
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> destination)
        {
            ThrowIfDisposed();
            if (position >= data.Length)
            {
                return 0;
            }
            int count = (int)Math.Min(destination.Length, data.Length - position);
            data.Span.Slice((int)position, count).CopyTo(destination);
            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(destination.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => position,
                SeekOrigin.End => data.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            long target = checked(start + offset);
            if (target < 0)
            {
                throw new IOException("Cannot seek before the beginning of the memory.");
            }
            position = target;
            return position;
        }

        public override void Flush() => ThrowIfDisposed();
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flush();
            return Task.CompletedTask;
        }
        public override void SetLength(long value) => throw new NotSupportedException("The memory stream is read-only.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The memory stream is read-only.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                disposed = true;
                data = default;
            }
            base.Dispose(disposing);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    }
}
