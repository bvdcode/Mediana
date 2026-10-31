// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264TemporalEncoderTests
    {
        [Theory]
        [InlineData(0, 26)]
        [InlineData(1, 26)]
        [InlineData(2, 38)]
        [InlineData(3, 18)]
        [InlineData(1, 0)]
        [InlineData(1, 51)]
        public async Task CompleteTemporalEncodingMatchesIndependentPixels(int mode, int qp) =>
            await H264TemporalEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), mode, qp);

        [Theory]
        [InlineData(-1, 16)]
        [InlineData(0, -1)]
        [InlineData(0, 65)]
        public void InvalidTemporalOptionsFailBeforeAllocating(int milliseconds, int range)
        {
            MediaBufferAllocator allocator = new(65536);
            Assert.Throws<ArgumentOutOfRangeException>(() => H264Encoder.Create(H264EncoderTests.Format(32, 32), new(1, 1000), allocator,
                new() { KeyFrameInterval = TimeSpan.FromMilliseconds(milliseconds), MotionSearchRange = range }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ResetDiscardsReferencesAndReplaysTheInitialIdrWhilePacketsStayOwned()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            encoder.TrySend(first);
            using MediaPacket initial = encoder.Receive().Value!;
            using VideoFrame second = H264EncoderTests.Frame(format, allocator, 1);
            encoder.TrySend(second);
            using MediaPacket inter = encoder.Receive().Value!;
            Assert.False(inter.IsKeyFrame);
            byte[] encoded = inter.Data.ToArray();
            encoder.Reset();
            encoder.TrySend(first);
            using MediaPacket replay = encoder.Receive().Value!;
            Assert.Equal(initial.Data.ToArray(), replay.Data.ToArray());
            encoder.Dispose();
            Assert.Equal(encoded, inter.Data.ToArray());
            initial.Dispose();
            inter.Dispose();
            replay.Dispose();
            first.Dispose();
            second.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationReleasesRetainedReferenceWithoutReleasingCallerStorage()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            using VideoFrame second = H264EncoderTests.Frame(format, allocator, 1);
            long held = allocator.AllocatedBytes;
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            encoder.TrySend(first);
            using MediaPacket packet = encoder.Receive().Value!;
            packet.Dispose();
            Assert.True(allocator.AllocatedBytes > held);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => encoder.TrySend(second, cancellation.Token));
            Assert.Equal(held, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            encoder.Reset();
            encoder.TrySend(first);
            using MediaPacket replay = encoder.Receive().Value!;
            Assert.True(replay.IsKeyFrame);
        }
    }
}
