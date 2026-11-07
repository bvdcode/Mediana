// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Tests;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class ContainerTests
    {
        [Theory]
        [InlineData("bframes", "avi", 1, 12)]
        [InlineData("bframes", "mp4", 1, 12)]
        [InlineData("fractional", "avi", 1001, 30000)]
        [InlineData("fractional", "mp4", 1001, 30000)]
        [InlineData("quarter", "avi", 1, 12)]
        [InlineData("quarter", "mp4", 1, 12)]
        public Task SharedApiDecodesEveryPictureSeeksBackwardPreviewsAndEncodesWholeAndInterval(
            string name, string extension, int numerator, int denominator) =>
            Mpeg4ContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name, extension, new(numerator, denominator));

        [Theory]
        [InlineData("bframes")]
        [InlineData("fractional")]
        [InlineData("quarter")]
        public async Task Mp4PresentationTimesAndTailDurationsMatchIndependentFrameMetadata(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", name + ".mp4");
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".frames.json"));
            string[] clock = reference.RootElement.GetProperty("streams")[0].GetProperty("time_base").GetString()!.Split('/');
            TimeBase timeBase = new(int.Parse(clock[0], CultureInfo.InvariantCulture), int.Parse(clock[1], CultureInfo.InvariantCulture));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                List<ContainerPacketTiming> timings = [];
                for (long index = 0; index < reader.GetPacketCount(0); index++) { timings.Add(reader.GetPacketTiming(0, index)); }
                timings.Sort((a, b) => a.PresentationTime!.Value.CompareTo(b.PresentationTime!.Value));
                JsonElement frames = reference.RootElement.GetProperty("frames");
                Assert.Equal(frames.GetArrayLength(), timings.Count);
                for (int index = 0; index < timings.Count; index++)
                {
                    Assert.Equal(0, timings[index].PresentationTime!.Value.CompareTo(new(frames[index].GetProperty("pts").GetInt64(), timeBase)));
                    MediaDuration duration = timings[index].Duration!.Value;
                    Assert.Equal(0, new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(frames[index].GetProperty("pkt_duration").GetInt64(), timeBase)));
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
