namespace Mediana.Codecs.Png
{
    internal class FixedMemoryWriteStream(Memory<byte> memory) : Stream
    {
        private bool disposed;
        public int WrittenCount { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed;
        public override long Length => WrittenCount;
        public override long Position
        {
            get => WrittenCount;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (buffer.Length > memory.Length - WrittenCount)
            {
                throw new InvalidOperationException("Compressed PNG data exceeded its reserved capacity.");
            }
            buffer.CopyTo(memory.Span.Slice(WrittenCount));
            WrittenCount += buffer.Length;
        }

        public override void Flush() => ObjectDisposedException.ThrowIf(disposed, this);
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
