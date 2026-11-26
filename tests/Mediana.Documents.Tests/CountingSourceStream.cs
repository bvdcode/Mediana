// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Tests
{
    internal class CountingSourceStream(Stream source, bool seekable = true, int maximumRead = int.MaxValue) : Stream
    {
        internal long BytesRead { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => seekable && !Disposed;
        public override bool CanWrite => false;
        public override long Length => CanSeek ? source.Length : throw new NotSupportedException();
        public override long Position { get => CanSeek ? source.Position : throw new NotSupportedException(); set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            int count = source.Read(buffer[..Math.Min(buffer.Length, maximumRead)]);
            BytesRead += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => CanSeek ? source.Seek(offset, origin) : throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) { source.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
