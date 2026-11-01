// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264RateControlTests
    {
        [Theory]
        [InlineData(0, 26, 8000, 250)]
        [InlineData(1, 26, 32000, 200)]
        [InlineData(2, 26, 48000, 200)]
        [InlineData(3, 26, 40000, 300)]
        [InlineData(1, 0, 32000, 200)]
        [InlineData(3, 51, 40000, 300)]
        public async Task CompleteRateControlledSequenceMatchesIndependentPixels(int mode, int qp, int bitRate, int bufferMilliseconds) =>
            await H264RateEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), mode, qp, bitRate, bufferMilliseconds);

        [Theory]
        [InlineData(0, 2000)]
        [InlineData(-1, 2000)]
        [InlineData(50000001, 1000)]
        [InlineData(1000, 0)]
        [InlineData(1000, -1)]
        [InlineData(1000, 1)]
        [InlineData(50000000, 2000)]
        public void InvalidRateOptionsFailBeforeAllocating(int rate, int milliseconds)
        {
            MediaBufferAllocator allocator = new(65536);
            Assert.Throws<ArgumentOutOfRangeException>(() => H264Encoder.Create(H264EncoderTests.Format(32, 32), new(1, 1000), allocator,
                new() { RateControl = new() { BitRate = rate, BufferDuration = TimeSpan.FromMilliseconds(milliseconds) } }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FractionalRefillHasNoPerFrameRoundingLossAndLargeGapsAreCapacityBounded()
        {
            TimeBase clock = new(1001, 30000);
            H264RateController controller = new(new() { BitRate = 30000, BufferDuration = TimeSpan.FromMilliseconds(100) }, clock, 26);
            long count = 0;
            for (int index = 0; index < 30000; index++)
            {
                MediaTimestamp timestamp = new(index - 15000, clock);
                H264FrameRateBudget budget = controller.Prepare(timestamp, 1, false);
                Assert.InRange(budget.MaximumBytes, 0, 375);
                count += budget.MaximumBytes;
                controller.Commit(timestamp, 26, budget.MaximumBytes, false);
            }
            Assert.Equal((3000L + 29999L * 1001) / 8, count);
            Assert.Equal(375, controller.Prepare(new(long.MaxValue, clock), 1, false).MaximumBytes);
            controller.Reset();
            Assert.Equal(375, controller.Prepare(new(long.MinValue, clock), 1, true).MaximumBytes);
        }

        [Fact]
        public void AnImpossibleBudgetFaultsWithoutPublishingOrRetainingInput()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            long inputBytes = allocator.AllocatedBytes;
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator,
                new() { Quantizer = 0, RateControl = new() { BitRate = 64, BufferDuration = TimeSpan.FromSeconds(1) } });
            NotSupportedException failure = Assert.Throws<NotSupportedException>(() => encoder.TrySend(frame));
            Assert.Contains("QP51", failure.Message);
            Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            Assert.Equal(inputBytes, allocator.AllocatedBytes);
            encoder.Reset();
            Assert.Throws<NotSupportedException>(() => encoder.TrySend(frame));
            Assert.Equal(inputBytes, allocator.AllocatedBytes);
        }

        [Fact]
        public void ResetRestoresRateHistoryAndRetainedPacketsRemainOwned()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            using VideoFrame second = H264EncoderTests.Frame(format, allocator, 1);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator,
                new() { RateControl = new() { BitRate = 12000 } });
            encoder.TrySend(first);
            using MediaPacket packet = encoder.Receive().Value!;
            encoder.TrySend(second);
            using MediaPacket inter = encoder.Receive().Value!;
            encoder.Reset();
            encoder.TrySend(first);
            using MediaPacket replay = encoder.Receive().Value!;
            Assert.Equal(packet.Data.ToArray(), replay.Data.ToArray());
            encoder.Dispose();
            Assert.NotEmpty(inter.Data.ToArray());
        }
    }
}
