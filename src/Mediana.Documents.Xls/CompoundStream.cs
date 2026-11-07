// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    internal class CompoundStream(long length, int sectorSize, int[] chain, Func<int, ReadOnlyMemory<byte>> sector) : Stream
    {
        private long _position;
        private bool _disposed;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int requested = (int)Math.Min(buffer.Length, length - _position);
            int copied = 0;
            while (copied < requested)
            {
                int page = (int)(_position / sectorSize), offset = (int)(_position % sectorSize);
                int count = Math.Min(requested - copied, sectorSize - offset);
                sector(chain[page]).Span.Slice(offset, count).CopyTo(buffer[copied..]);
                copied += count;
                _position += count;
            }
            return copied;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (next < 0 || next > length) { throw new InvalidDataException("CFB stream seek is outside its logical length."); }
            return _position = next;
        }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
    }
}
