// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Video;

namespace Mediana.Containers.Avi.Tests
{
    public class Mpeg4AviIndexTests
    {
        [Theory]
        [InlineData("bframes", 24, 1, 12, 96, 80)]
        [InlineData("fractional", 30, 1001, 30000, 96, 80)]
        [InlineData("quarter", 24, 1, 12, 128, 96)]
        public async Task OpeningReadsClockPrefixesAndIndexWithoutReadingTheCompleteMovie(
            string name, int count, int numerator, int denominator, int width, int height)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mpeg4Visual", name + ".avi"));
            await using AviTestSource source = new(bytes, 3);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                Assert.Equal("mpeg4", reader.Streams[0].CodecId);
                Assert.Equal(count, reader.GetPacketCount(0));
                Mpeg4VisualConfiguration configuration = Mpeg4VisualConfiguration.Read(reader.Streams[0].Configuration.Span);
                Assert.Equal(width, configuration.Width);
                Assert.Equal(height, configuration.Height);
                Assert.True(source.ReadBytes < bytes.Length * 2 / 3, $"Read {source.ReadBytes} of {bytes.Length} bytes at open.");
                long reads = source.ReadBytes;
                List<MediaTimestamp> display = [];
                for (int index = 0; index < count; index++)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(index, timing.DecodeTime!.Value.Value);
                    display.Add(timing.PresentationTime!.Value);
                }
                display.Sort((a, b) => a.CompareTo(b));
                for (int index = 0; index < count; index++)
                {
                    MediaTimestamp target = new(index, new(numerator, denominator));
                    Assert.Equal(0, display[index].CompareTo(target));
                    ContainerSeekPoint point = reader.FindSeekPoint(0, target, count)!.Value;
                    Assert.True(reader.GetPacketTiming(0, point.PacketIndex).IsKeyFrame);
                    Assert.True(point.PresentationTime.CompareTo(target) <= 0);
                }
                Assert.Equal(reads, source.ReadBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(4097)]
        [InlineData(16500)]
        public async Task ClockIndexAllocationFailureReleasesTheCompleteReader(int limit)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mpeg4Visual", "bframes.avi"));
            await using AviTestSource source = new(bytes);
            MediaBufferAllocator allocator = new(limit);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await AviReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
