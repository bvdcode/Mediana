// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp4AudioSampleRangeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FractionalSampleBoundariesAndEmptyEditDelayRemainExact(bool wide)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a");
            byte[] data = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(path), wide,
                (1, -1, 65536), (1, 1024, 65536));
            MediaBufferAllocator allocator = new(16384);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Mp4AudioSampleRange range = Mp4AudioSampleRange.FromEditList(reader.Tracks[0], 8000);
            Assert.Equal(171, range.StartSample);
            Assert.Equal(8, range.SampleCount);
            Assert.Equal(new MediaTimestamp(25, new TimeBase(1, 24000)), range.PresentationTime);
            Assert.Equal(new MediaTimestamp(49, new TimeBase(1, 24000)), range.GetPresentationEnd(8000));
            Mp4AudioSampleRange ordinary = Mp4AudioSampleRange.FromEditList(reader.Tracks[0], 48000);
            Assert.Equal(1024, ordinary.StartSample);
            Assert.Equal(48, ordinary.SampleCount);
            Assert.Equal(new MediaTimestamp(1, new TimeBase(1, 1000)), ordinary.PresentationTime);
            Assert.Equal(new MediaTimestamp(2, new TimeBase(1, 1000)), ordinary.GetPresentationEnd(48000));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task MissingNonlinearAndReorderedEditsAreExplicitlyUnsupported(int mode)
        {
            (long Duration, long MediaTime, int Rate)[] edits = mode switch
            {
                0 => [],
                1 => [(1, 0, 32768)],
                2 => [(1, 1024, 65536), (1, 0, 65536)],
                3 => [(1, -1, 65536)],
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            byte[] data = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a")), false, edits);
            MediaBufferAllocator allocator = new(16384);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Throws<NotSupportedException>(() => Mp4AudioSampleRange.FromEditList(reader.Tracks[0], 48000));
        }

        [Fact]
        public async Task EmptyEditSelectionIsEmptyAndVideoOrUnrepresentableClocksAreRejected()
        {
            byte[] data = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a")), false, (0, 1024, 65536));
            MediaBufferAllocator allocator = new(16384);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(0, Mp4AudioSampleRange.FromEditList(reader.Tracks[0], 48000).SampleCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => Mp4AudioSampleRange.FromEditList(reader.Tracks[0], 0));
            Assert.Throws<NotSupportedException>(() => Mp4AudioSampleRange.FromEditList(reader.Tracks[0], int.MaxValue));
            await using TrackingRandomAccessSource videoSource = new(await Mp4TestData.ReadAsync("avc-tail.mov"));
            await using Mp4Reader video = await Mp4Reader.OpenAsync(videoSource, allocator);
            Assert.Throws<NotSupportedException>(() => Mp4AudioSampleRange.FromEditList(video.Tracks[0], 48000));
        }

        [Fact]
        public void PresentationEndChecksCountsRatesClocksAndOverflowWithoutIntermediateNarrowing()
        {
            Mp4AudioSampleRange range = new(0, 1, new(long.MaxValue, new(1, 1)));
            Assert.Throws<OverflowException>(() => range.GetPresentationEnd(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => range.GetPresentationEnd(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => range.GetPresentationEnd(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4AudioSampleRange(0, -1, new(0, new(1, 1))).GetPresentationEnd(48000));
            Assert.Throws<ArgumentException>(() => default(Mp4AudioSampleRange).GetPresentationEnd(48000));
            Assert.Throws<NotSupportedException>(() => new Mp4AudioSampleRange(0, 1, new(0, new(1, int.MaxValue))).GetPresentationEnd(48000));
            Assert.Equal(new MediaTimestamp(-1, new(1, 1)),
                new Mp4AudioSampleRange(0, long.MaxValue, new(long.MinValue, new(1, 1))).GetPresentationEnd(1));
        }

        [Theory]
        [InlineData(8000, 1, 1, 997)]
        [InlineData(44100, -3, 1, 7)]
        [InlineData(48000, 113, 7, 997)]
        [InlineData(48000, -1024, 1, 48000)]
        public void PresentationEndMatchesArbitraryPrecisionRationalAddition(int rate, long origin, int numerator, int denominator)
        {
            foreach (long count in new long[] { 0, 1, 1025, 1000000000 })
            {
                Mp4AudioSampleRange range = new(0, count, new(origin, new(numerator, denominator)));
                MediaTimestamp end = range.GetPresentationEnd(rate);
                BigInteger expected = (BigInteger)origin * numerator * rate + (BigInteger)count * denominator;
                BigInteger actual = (BigInteger)end.Value * end.TimeBase.Numerator * denominator * rate;
                Assert.Equal(expected * end.TimeBase.Denominator, actual);
            }
        }
    }
}
