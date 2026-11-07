// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.OpenXml
{
    internal class OpenXmlPartStream(Stream source, long length, CancellationToken token) : Stream
    {
        private long _position;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            if (buffer.IsEmpty) { return 0; }
            if (_position == length)
            {
                if (source.ReadByte() != -1) { throw new InvalidDataException("OPC part exceeds its declared length."); }
                return 0;
            }
            int count = source.Read(buffer[..(int)Math.Min(buffer.Length, length - _position)]);
            if (count == 0) { throw new InvalidDataException("Truncated OPC part."); }
            _position += count;
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { source.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
