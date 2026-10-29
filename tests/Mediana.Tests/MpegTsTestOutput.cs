// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class MpegTsTestOutput : Stream
    {
        public MemoryStream Bytes { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate { get; init; }
        public int? FailOnWrite { get; init; }
        public bool FailOnFlush { get; init; }
        public bool WasDisposed { get; private set; }
        public int Writes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !WasDisposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            Entered.TrySetResult();
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Writes >= FailOnWrite)
            {
                await Bytes.WriteAsync(buffer[..Math.Min(17, buffer.Length)], cancellationToken);
                throw new IOException("The output failed during a partial TS packet write.");
            }
            await Bytes.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailOnFlush)
            {
                throw new IOException("The output failed during flush.");
            }
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing)
            {
                Bytes.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
