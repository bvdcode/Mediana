// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Raster.Tests
{
    internal class JpegTestOutput : Stream
    {
        private readonly MemoryStream data = new();
        internal CancellationTokenSource? CancelAfterWrite { get; init; }
        internal bool FailWrite { get; init; }
        internal TaskCompletionSource? ContinueWriting { get; init; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal byte[] Bytes => data.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (ContinueWriting is not null) { await ContinueWriting.Task.WaitAsync(cancellationToken); }
            if (FailWrite) { throw new IOException("Destination write failed."); }
            await data.WriteAsync(buffer, cancellationToken);
            if (CancelAfterWrite is not null) { await CancelAfterWrite.CancelAsync(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { data.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
