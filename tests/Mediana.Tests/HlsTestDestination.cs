// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class HlsTestDestination : MemoryStream
    {
        public bool Disposed { get; private set; }
        public bool FailWrites { get; init; }
        public TaskCompletionSource? ReleaseWrites { get; init; }
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteEntered.TrySetResult();
            if (ReleaseWrites is not null)
            {
                await ReleaseWrites.Task.WaitAsync(cancellationToken);
            }
            if (FailWrites)
            {
                throw new IOException("The HLS destination rejected its write.");
            }
            await base.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
