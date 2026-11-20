// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Streaming.Hls
{
    /// <summary>Indexed audio packet delivery to HLS destinations without re-encoding.
    /// Owns its reader and pending packet, borrows input and destinations. Operations must not overlap.</summary>
    public class HlsCopyAudioSource : HlsAudioSource
    {
        private readonly ContainerSession reader;
        private readonly long packetCount;
        private long sample;
        private MediaPacket? pending;
        private bool disposed;
        public override long PacketsWritten { get; protected set; }
        public override EncodedStreamInfo Stream { get; }

        private HlsCopyAudioSource(ContainerSession reader, EncodedStreamInfo stream, long packetCount)
        {
            this.reader = reader;
            this.packetCount = packetCount;
            Stream = stream;
        }

        /// <summary>Opens MP4/MOV with movie-timeline timestamps. Use the engine overload for registered container formats.</summary>
        public static ValueTask<HlsCopyAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            MediaBufferAllocator allocator, CancellationToken token) =>
            OpenAsync(new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new()
            { TimestampMode = Mp4TimestampMode.LinearMovie })).Build(), source, streamIndex, allocator, token);

        public static async ValueTask<HlsCopyAudioSource> OpenAsync(MediaEngine engine, IRandomAccessByteSource source, int streamIndex,
            MediaBufferAllocator allocator, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ContainerSession reader = await engine.OpenContainerAsync(source, allocator,
                new() { BufferBudgetBytes = allocator.LimitBytes }, token).ConfigureAwait(false);
            bool success = false;
            try
            {
                EncodedStreamInfo stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == streamIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(streamIndex));
                if (stream.Kind != MediaKind.Audio || stream.CodecId is not ("aac" or "mp3"))
                {
                    throw new NotSupportedException("HLS audio copy requires a nonempty AAC-LC or MPEG-1/2 Layer III track.");
                }
                long count = reader.GetPacketCount(streamIndex);
                if (count == 0) { throw new NotSupportedException("HLS audio copy requires a nonempty audio track."); }
                HlsCopyAudioSource result = new(reader, stream, count);
                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token)
        {
            if (sample == packetCount)
            {
                return null;
            }
            MediaPacket packet = await reader.ReadPacketAtAsync(Stream.StreamIndex, sample, token).ConfigureAwait(false);
            sample++;
            if (packet.DecodeTime is null)
            {
                try
                {
                    MediaTimestamp time = packet.PresentationTime
                        ?? throw new NotSupportedException("HLS audio copy requires a known packet timestamp.");
                    return packet.RetainWithTiming(time, time, packet.Duration);
                }
                finally { packet.Dispose(); }
            }
            return packet;
        }

        public override async ValueTask WriteBeforeAsync(MediaTimestamp? boundary, IReadOnlyList<IContainerWriter> destinations, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                pending ??= await ReadPacketAsync(token).ConfigureAwait(false);
                if (pending is null || (boundary is MediaTimestamp limit && pending.DecodeTime!.Value.CompareTo(limit) >= 0))
                {
                    return;
                }
                foreach (IContainerWriter destination in destinations)
                {
                    await destination.WritePacketAsync(pending, token).ConfigureAwait(false);
                }
                pending.Dispose();
                pending = null;
                PacketsWritten++;
            }
        }


        public override async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                pending?.Dispose();
                pending = null;
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            GC.SuppressFinalize(this);
        }
    }
}
