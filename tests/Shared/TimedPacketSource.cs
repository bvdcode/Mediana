// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    internal class TimedPacketSource(MediaBufferAllocator allocator, int index, TimeBase clock, long?[] timestamps) : IEncodedPacketSource
    {
        private int position;
        public bool IsDisposed { get; private set; }
        public bool WrongStream { get; init; }
        public bool FailReads { get; init; }
        public Action? AfterRead { get; init; }
        public int Reads { get; private set; }
        public EncodedStreamInfo Stream { get; } = new(index, MediaKind.Audio, "fixture.timed",
            new("none", "fixture.timed", EncodedPacketBoundary.ArbitraryBytes), clock);

        public ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Reads++;
            if (FailReads)
            {
                throw new IOException("Packet source failure.");
            }
            if (position == timestamps.Length)
            {
                return ValueTask.FromResult<MediaPacket?>(null);
            }
            using WritableMediaBuffer writable = allocator.Allocate(1);
            writable.Memory.Span[0] = (byte)(index * 16 + position);
            using MediaBuffer buffer = writable.Publish();
            MediaTimestamp? time = timestamps[position++] is long value ? new(value, clock) : null;
            MediaPacket packet = new(WrongStream ? index + 1 : index, buffer, time, time);
            AfterRead?.Invoke();
            return ValueTask.FromResult<MediaPacket?>(packet);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
