// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioTimeWindowTests
    {
        [Theory]
        [InlineData(AudioStorageLayout.Interleaved, 8000)]
        [InlineData(AudioStorageLayout.Interleaved, 44100)]
        [InlineData(AudioStorageLayout.Interleaved, 48000)]
        [InlineData(AudioStorageLayout.Interleaved, 96000)]
        [InlineData(AudioStorageLayout.Planar, 8000)]
        [InlineData(AudioStorageLayout.Planar, 44100)]
        [InlineData(AudioStorageLayout.Planar, 48000)]
        [InlineData(AudioStorageLayout.Planar, 96000)]
        public async Task ExactPresentationIntersectionMatchesIndependentSampleEnumeration(AudioStorageLayout layout, int rate)
        {
            foreach (MediaTimestamp origin in new MediaTimestamp[] { new(0, new(1, 1)), new(-7, new(1, rate)), new(1, new(1, 7)), new(-1, new(1, 7)), new(5, new(3, 11)) })
            {
                await AudioTimeWindowTestInput.VerifyAsync(layout, rate, origin);
            }
        }

        [Fact]
        public async Task UnknownTimestampsFailWithoutRetainingInputAndFailureIsTerminal()
        {
            MediaBufferAllocator allocator = new(1024);
            AudioSignalSource source = new(allocator, 8000, 4, (index, _) => index, [4]) { Timestamp = _ => null };
            await using AudioTrimmer window = AudioTrimmer.CreateTimeWindow(source.ReadAsync, new(0, new(1, 1)), null, allocator);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await window.ReadFrameAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await window.ReadFrameAsync());
        }

        [Fact]
        public void InvalidOrReversedTimesAreRejectedBeforeReading()
        {
            MediaBufferAllocator allocator = new(1024);
            AudioSignalSource source = new(allocator, 8000, 4, (index, _) => index, [4]);
            Assert.Throws<ArgumentException>(() => AudioTrimmer.CreateTimeWindow(source.ReadAsync, default, null, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioTrimmer.CreateTimeWindow(source.ReadAsync, new(2, new(1, 1)), new(1, new(1, 1)), allocator));
            Assert.Equal(0, source.BlocksRead);
        }

        [Fact]
        public void LargeRationalBoundaryKeepsExactSamplePositionAndRejectsOutOfRangeIndices()
        {
            AudioSampleClock clock = new(new(-1, new(1, int.MaxValue)), int.MaxValue, int.MaxValue);
            long position = clock.PositionAtOrAfter(new(int.MaxValue, new(int.MaxValue - 1, int.MaxValue)));
            Assert.Equal((long)(int.MaxValue - 1) * int.MaxValue + 1, position);
            Assert.Throws<OverflowException>(() => clock.PositionAtOrAfter(new(long.MaxValue, new(1, 1))));
        }
    }
}
