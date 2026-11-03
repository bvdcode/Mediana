// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsTargetDurationTestInput
    {
        public static async Task VerifyAsync()
        {
            MediaBufferAllocator allocator = new(4096);
            EncodedStreamInfo audio = new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000), [0x11, 0x88]);
            await using (AudioHlsWriter writer = AudioHlsWriter.Create(audio,
                (_, _) => ValueTask.FromResult<Stream>(new MemoryStream()), allocator,
                new() { TargetDuration = new(6, new(1, 1)), PlaylistTargetDurationSeconds = 6 }))
            {
                for (int frame = 0; frame < 282; frame++)
                {
                    using WritableMediaBuffer storage = allocator.Allocate(1);
                    storage.Span.Clear();
                    using MediaBuffer buffer = storage.Publish();
                    MediaTimestamp time = new(frame * 1024, audio.TimeBase);
                    using MediaPacket packet = new(0, buffer, time, time);
                    await writer.WritePacketAsync(packet);
                }
                await writer.CompleteAsync();
                string playlist = writer.CreateVodPlaylist(_ => "audio.ts");
                HlsAudioRendition rendition = writer.CreateRendition("audio.m3u8", "audio", "Main");
                if (writer.Segments.Count != 1 || writer.Segments[0].Duration != new MediaDuration(288768, audio.TimeBase) ||
                    !playlist.Contains("#EXT-X-TARGETDURATION:6\n", StringComparison.Ordinal) ||
                    !playlist.Contains("#EXTINF:6.016,\n", StringComparison.Ordinal) || rendition.Bandwidth != rendition.AverageBandwidth)
                {
                    throw new InvalidDataException("The common HLS target duration changed coded packet extent or rejected legal nearest-integer rounding.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The common HLS target-duration check retained media buffers.");
            }
        }
    }
}
