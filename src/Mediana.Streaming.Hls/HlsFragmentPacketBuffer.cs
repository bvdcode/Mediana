// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Streaming.Hls
{
    internal class HlsFragmentPacketBuffer(Mp4VideoTrackDefinition track, Stream destination,
        MediaBufferAllocator allocator, uint sequence) : IContainerWriter
    {
        private readonly List<MediaPacket> packets = [];
        private readonly H264RandomAccessInspector inspector = new(track.Stream);
        private bool completed;
        private bool disposed;

        public long BytesWritten { get; private set; }

        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed) { throw new InvalidOperationException("The fragment buffer is complete."); }
            if (packets.Count == 0 && !inspector.IsIdr(packet, cancellationToken))
            {
                throw new InvalidDataException("The encoded HLS segment does not begin with an independently decodable AVC IDR picture.");
            }
            MediaPacket retained = packet.Retain();
            bool added = false;
            try
            {
                packets.Add(retained);
                added = true;
            }
            finally { if (!added) { retained.Dispose(); } }
            return ValueTask.CompletedTask;
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed) { throw new InvalidOperationException("The fragment buffer is complete."); }
            completed = true;
            BytesWritten = await Mp4VideoFragmentWriter.WriteFragmentAsync(track, packets, sequence,
                destination, allocator, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                foreach (MediaPacket packet in packets) { packet.Dispose(); }
                packets.Clear();
            }
            return ValueTask.CompletedTask;
        }
    }
}
