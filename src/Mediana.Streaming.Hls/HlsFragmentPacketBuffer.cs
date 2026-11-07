// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal class HlsFragmentPacketBuffer(Mp4VideoTrackDefinition track, IReadOnlyList<Mp4TrackDefinition> tracks, Stream destination,
        MediaBufferAllocator allocator, uint sequence, HlsVideoWindow window) : IContainerWriter
    {
        private readonly Mp4EncodedFragmentPacketWriter output = new(tracks, sequence, destination, allocator);
        private bool first = true;
        private readonly H264RandomAccessInspector inspector = new(track.Stream);
        private bool completed;
        private bool disposed;
        private MediaTimestamp end = window.Start;

        public long BytesWritten { get; private set; }

        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed) { throw new InvalidOperationException("The fragment buffer is complete."); }
            if (packet.StreamIndex != track.Stream.StreamIndex)
            {
                await output.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (first && !inspector.IsIdr(packet, cancellationToken))
            {
                throw new InvalidDataException("The encoded HLS segment does not begin with an independently decodable AVC IDR picture.");
            }
            if (packet.PresentationTime != end || packet.Duration is not MediaDuration duration || duration.Value == 0)
            { throw new InvalidDataException("An HLS video fragment requires contiguous timestamped frames."); }
            TimeBase clock = TimeBase.Common(end.ReduceClock().TimeBase, new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock().TimeBase);
            MediaTimestamp next = new(checked(end.Rescale(clock, TimestampRounding.TowardZero).Value +
                new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value), clock);
            if (next.CompareTo(window.End) > 0) { throw new InvalidDataException("The encoded frame extends beyond its HLS window."); }
            await output.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
            end = next;
            first = false;
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed) { throw new InvalidOperationException("The fragment buffer is complete."); }
            if (end != window.End) { throw new InvalidDataException("The encoded video does not fill its HLS window."); }
            completed = true;
            await output.CompleteAsync(cancellationToken).ConfigureAwait(false);
            BytesWritten = output.BytesWritten;
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                return output.DisposeAsync();
            }
            return ValueTask.CompletedTask;
        }
    }
}
