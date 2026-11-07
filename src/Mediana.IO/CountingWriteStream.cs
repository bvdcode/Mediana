// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    /// <summary>Counts successful writes while borrowing the destination. Includes repeated writes after seeking.
    /// Supports the destination's seek capability; calls must not overlap. Disposal leaves the destination open.</summary>
    public class CountingWriteStream(Stream destination) : Stream
    {
        private readonly Stream destination = destination ?? throw new ArgumentNullException(nameof(destination));
        private bool disposed;
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => !disposed && destination.CanSeek;
        public override bool CanWrite => !disposed && destination.CanWrite;
        public override long Length { get { RequireActive(); return destination.Length; } }
        public override long Position
        {
            get { RequireActive(); return destination.Position; }
            set { RequireActive(); destination.Position = value; }
        }
        public override void Flush() { RequireActive(); destination.Flush(); }
        public override Task FlushAsync(CancellationToken cancellationToken) { RequireActive(); return destination.FlushAsync(cancellationToken); }
        public override long Seek(long offset, SeekOrigin origin) { RequireActive(); return destination.Seek(offset, origin); }
        public override void SetLength(long value) { RequireActive(); destination.SetLength(value); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("This stream only writes output.");
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            RequireActive();
            destination.Write(buffer);
            BytesWritten = checked(BytesWritten + buffer.Length);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            RequireActive();
            await destination.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten = checked(BytesWritten + buffer.Length);
        }
        protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
        private void RequireActive() => ObjectDisposedException.ThrowIf(disposed, this);
    }
}
