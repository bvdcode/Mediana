// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Cli
{
    internal class HlsAudioSource(Mp4Reader reader, EncodedStreamInfo stream) : IAsyncDisposable
    {
        private int sample;
        public EncodedStreamInfo Stream { get; } = stream;
        public int PacketsCopied => sample;

        public static async ValueTask<HlsAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
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
                HlsAudioSource result = new(reader, stream);
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

        public async ValueTask CopyBeforeAsync(MediaTimestamp? boundary, IReadOnlyList<HlsVideoRendition> renditions, CancellationToken token)
        {
            while (sample < reader.Tracks[Stream.StreamIndex].SampleCount)
            {
                token.ThrowIfCancellationRequested();
                Mp4SampleInfo info = reader.GetSampleInfo(Stream.StreamIndex, sample, token);
                if (boundary is MediaTimestamp limit && info.DecodeTime.CompareTo(limit) >= 0)
                {
                    break;
                }
                using MediaPacket packet = await reader.ReadPacketAtAsync(Stream.StreamIndex, sample, token);
                foreach (HlsVideoRendition rendition in renditions)
                {
                    await rendition.Writer.WritePacketAsync(packet, token);
                }
                sample++;
            }
        }

        public ValueTask DisposeAsync() => reader.DisposeAsync();
    }
}
