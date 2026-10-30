// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal class HlsCopyAudioSource(Mp4Reader reader, EncodedStreamInfo stream) : HlsAudioSource
    {
        private int sample;
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

        protected override async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token)
        {
            if (sample == reader.Tracks[Stream.StreamIndex].SampleCount)
            {
                return null;
            }
            MediaPacket packet = await reader.ReadPacketAtAsync(Stream.StreamIndex, sample, token);
            sample++;
            return packet;
        }

        protected override ValueTask DisposeCoreAsync() => reader.DisposeAsync();
    }
}
