// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class IndexedTsContinuityTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task IndependentSegmentsKeepCountersClocksTimestampsAndPayloadsContinuous(bool includeAudio)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            using MemoryStream joined = new();
            await using (Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0,
                includeAudio ? 1 : null, HlsSessionTestInput.Options()))
            {
                foreach (Mp4HlsSegment segment in session.Segments)
                {
                    await session.WriteSegmentAsync(segment.Number, joined);
                }
            }
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            List<(int Stream, byte[] Data, long Pts, long Dts)> packets = [];
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    if (includeAudio || packet.StreamIndex == 0)
                    {
                        packets.Add((packet.StreamIndex, packet.Data.ToArray(), Ticks(packet.PresentationTime), Ticks(packet.DecodeTime)));
                    }
                }
            }
            MpegTsTestInput.Verify(joined.ToArray(), includeAudio ? reader.Streams : [reader.Streams[0]], packets);
        }

        private static long Ticks(MediaTimestamp? time) => time!.Value.Rescale(new(1, 90000), TimestampRounding.NearestEven).Value + 90000;
    }
}
