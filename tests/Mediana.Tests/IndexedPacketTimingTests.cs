// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class IndexedPacketTimingTests
    {
        [Theory]
        [InlineData("FrameRate", "frame-rate-cfr.mp4")]
        [InlineData("FrameRate", "frame-rate-vfr.mp4")]
        [InlineData("FrameRate", "frame-rate-edited.mp4")]
        [InlineData("Matroska", "avc-aac-attachment.mkv")]
        public async Task ClockQueriesReadOnlyMetadataAndPreserveAllSequentialPacketCursors(string group, string file)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", group, file));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using (IIndexedPacketTimingReader reader = group switch
            {
                "FrameRate" => await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }),
                "Matroska" => await MatroskaReader.OpenAsync(source, allocator),
                _ => throw new ArgumentOutOfRangeException(nameof(group))
            })
            {
                source.Reads.Clear();
                foreach (EncodedStreamInfo stream in reader.Streams)
                {
                    for (long index = 0; index < reader.GetPacketCount(stream.StreamIndex); index++)
                    {
                        _ = reader.GetPacketTiming(stream.StreamIndex, index);
                    }
                }
                Assert.Empty(source.Reads);
                Dictionary<int, long> positions = reader.Streams.ToDictionary(stream => stream.StreamIndex, _ => 0L);
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null) { break; }
                    ContainerPacketTiming timing = reader.GetPacketTiming(packet.StreamIndex, positions[packet.StreamIndex]++);
                    Assert.Equal(timing.PresentationTime, packet.PresentationTime);
                    Assert.Equal(timing.DecodeTime, packet.DecodeTime);
                    Assert.Equal(timing.Duration, packet.Duration);
                    Assert.Equal(timing.IsKeyFrame, packet.IsKeyFrame);
                }
                Assert.All(reader.Streams, stream => Assert.Equal(reader.GetPacketCount(stream.StreamIndex), positions[stream.StreamIndex]));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

    }
}
