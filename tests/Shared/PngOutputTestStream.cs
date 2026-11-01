// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class PngOutputTestStream : Stream
    {
        private readonly MemoryStream output = new();
        private bool disposed;
        public Func<int, ReadOnlyMemory<byte>, CancellationToken, ValueTask>? BeforeWrite { get; set; }
        public int Writes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public byte[] ToArray() => output.ToArray();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Writes++;
            if (BeforeWrite is not null)
            {
                await BeforeWrite(Writes, buffer, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush() => throw new InvalidOperationException("The destination must not be flushed.");
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Only asynchronous writes are allowed.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            if (disposing)
            {
                output.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
