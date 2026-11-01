// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class FragmentedReadStream(byte[] data, int maximumRead) : Stream
    {
        private readonly MemoryStream input = new(data, writable: false);
        internal int SyncReads { get; private set; }
        internal int AsyncReads { get; private set; }
        internal int? ReportedCount { get; set; }
        internal Action? BeforeRead { get; set; }
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(Span<byte> buffer)
        {
            SyncReads++;
            return ReadCore(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncReads++;
            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        private int ReadCore(Span<byte> buffer)
        {
            BeforeRead?.Invoke();
            if (ReportedCount is int count)
            {
                return count;
            }
            return input.Read(buffer[..Math.Min(buffer.Length, maximumRead)]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                input.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
