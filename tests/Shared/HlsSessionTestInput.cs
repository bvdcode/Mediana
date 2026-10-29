// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsSessionTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using FileStream input = File.OpenRead(Path.Combine(root, "avc-segments.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            byte[][] outputs = new byte[3][];
            Mp4HlsSession session;
            await using (session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, Options()))
            {
                if (session.Streams.Count != 2 || session.Segments.Count != 3)
                {
                    throw new InvalidDataException("The HLS session did not index the selected AVC/AAC streams.");
                }
                int[] audioCounts = [123, 141, 75];
                int firstAudio = 0;
                for (int number = 0; number < 3; number++)
                {
                    Mp4HlsSegment segment = session.Segments[number];
                    Mp4HlsAudioRange audio = segment.Audio!.Value;
                    if (segment.Number != number || segment.Video.FirstSample != number * 15 ||
                        audio.StreamIndex != 1 || audio.FirstSample != firstAudio || audio.SampleCount != audioCounts[number] ||
                        audio.PresentationStart != new MediaTimestamp(firstAudio * 1024L - 1024, new(1, 48000)))
                    {
                        throw new InvalidDataException("HLS video or audio sample ranges differ from independent decode-order references.");
                    }
                    firstAudio += audioCounts[number];
                }
                string expected = "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n" +
                    "#EXTINF:3,\nsegment-0.ts\n#EXTINF:3,\nsegment-1.ts\n#EXTINF:1.2,\nsegment-2.ts\n#EXT-X-ENDLIST\n";
                if (session.CreateVodPlaylist(number => $"segment-{number}.ts") != expected)
                {
                    throw new InvalidDataException("The VOD playlist does not describe the actual video windows.");
                }
                foreach (int number in new[] { 2, 0, 1, 2 })
                {
                    using MemoryStream destination = new();
                    await session.WriteSegmentAsync(number, destination);
                    byte[] bytes = destination.ToArray();
                    if (!destination.CanWrite || (outputs[number] is not null && !outputs[number].AsSpan().SequenceEqual(bytes)))
                    {
                        throw new InvalidDataException("Repeated HLS output differs or closes its borrowed stream.");
                    }
                    outputs[number] = bytes;
                }
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead || session.Segments[2].Number != 2)
            {
                throw new InvalidOperationException("The HLS session retained media buffers or closed its borrowed source.");
            }
            List<(int Stream, byte[] Data, long Pts, long Dts)> packets = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        packets.Add((packet.StreamIndex, packet.Data.ToArray(), Ticks(packet.PresentationTime), Ticks(packet.DecodeTime)));
                    }
                }
                MpegTsTestInput.Verify(outputs.SelectMany(bytes => bytes).ToArray(), reader.Streams, packets);
            }
            if (packets.Count != 375 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("The HLS output must preserve all 36 AVC and 339 AAC packets without retaining buffers.");
            }
        }

        public static Mp4HlsOptions Options() => new() { Planning = new() { TargetDuration = new(9, new TimeBase(1, 4)) } };

        private static long Ticks(MediaTimestamp? value) => checked(value!.Value.Rescale(new(1, 90000), TimestampRounding.NearestEven).Value + 90000);
    }
}
