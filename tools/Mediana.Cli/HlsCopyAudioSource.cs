// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Cli
{
    internal class HlsCopyAudioSource(Mp4Reader reader, EncodedStreamInfo stream) : HlsAudioSource
    {
        private int sample;
        private MediaPacket? pending;
        private bool disposed;
        public override long PacketsWritten { get; protected set; }
        public override EncodedStreamInfo Stream { get; } = stream;

        public static async ValueTask<HlsCopyAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }, token);
            bool success = false;
            try
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, reader.Streams.Count);
                EncodedStreamInfo stream = reader.Streams[streamIndex];
                if (stream.Kind != MediaKind.Audio || stream.CodecId is not ("aac" or "mp3") || reader.Tracks[streamIndex].SampleCount == 0)
                {
                    throw new NotSupportedException("HLS audio copy requires a nonempty AAC-LC or MPEG-1/2 Layer III track.");
                }
                HlsCopyAudioSource result = new(reader, stream);
                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    await reader.DisposeAsync();
                }
            }
        }

        private async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token)
        {
            if (sample == reader.Tracks[Stream.StreamIndex].SampleCount)
            {
                return null;
            }
            MediaPacket packet = await reader.ReadPacketAtAsync(Stream.StreamIndex, sample, token);
            sample++;
            return packet;
        }

        public override async ValueTask WriteBeforeAsync(MediaTimestamp? boundary, IReadOnlyList<IContainerWriter> destinations, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                pending ??= await ReadPacketAsync(token);
                if (pending is null || (boundary is MediaTimestamp limit && pending.DecodeTime!.Value.CompareTo(limit) >= 0))
                {
                    return;
                }
                foreach (IContainerWriter destination in destinations)
                {
                    await destination.WritePacketAsync(pending, token);
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
                await reader.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }
    }
}
