// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class Mp4FrameRateTests
    {
        [Fact]
        public async Task RealFractionalVariableAndEditedRatesMatchIndependentReferences() =>
            await Mp4FrameRateTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HeaderDurationsAndTimestampModesDoNotReplaceIndexedRate(bool wide)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            Mp4TestData.SetUInt32(data, "mvhd", 16, 0);
            Mp4TestData.SetUInt32(data, "tkhd", 20, uint.MaxValue);
            Mp4TestData.SetUInt32(data, "mdhd", 16, uint.MaxValue);
            if (wide)
            {
                data = Mp4TestData.Use64BitChunkOffsets(Mp4TestData.Use64BitTimeHeaders(data));
            }
            await VerifyAsync(data, new FrameRate(5, 1));
        }

        [Theory]
        [InlineData(0U)]
        [InlineData(2048U)]
        [InlineData(uint.MaxValue)]
        public async Task ZeroAndLargeSampleDurationsRetainTheirMeaning(uint duration)
        {
            byte[] data = Mp4SampleTableTestData.WithTimingRuns(await Mp4TestData.ReadAsync("avc-tail.mov"), (6, duration));
            FrameRate? expected = null;
            if (duration != 0)
            {
                expected = new FrameRate(10240, duration);
            }
            await VerifyAsync(data, expected);
        }

        [Fact]
        public async Task VariableRunsUseTheirWholeCountAndDuration()
        {
            byte[] data = Mp4SampleTableTestData.WithTimingRuns(await Mp4TestData.ReadAsync("avc-tail.mov"), (2, 1001), (4, 3000));
            await VerifyAsync(data, new FrameRate(30720, 7001));
        }

        private static async Task VerifyAsync(byte[] data, FrameRate? expected)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
            {
                await using TrackingRandomAccessSource source = new(data, 7);
                await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode }))
                {
                    Assert.Equal(expected, reader.Tracks[0].AverageFrameRate);
                    Assert.Equal(expected, reader.Info.StreamInfo![0].AverageFrameRate);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}
