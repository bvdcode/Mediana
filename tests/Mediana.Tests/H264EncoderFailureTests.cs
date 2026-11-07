// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264EncoderFailureTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PacketLimitAndCancellationFaultTheEncoderWithoutReleasingCallerStorage(bool cancel)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            long held = allocator.AllocatedBytes;
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator,
                new() { MaximumPacketBytes = cancel ? 65536 : 8 });
            using CancellationTokenSource cancellation = new();
            if (cancel)
            {
                cancellation.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => encoder.TrySend(input, cancellation.Token));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => encoder.TrySend(input));
            }
            Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            Assert.Equal(held, allocator.AllocatedBytes);
            Assert.NotEmpty(input.Data.ToArray());
            encoder.Reset();
            if (cancel)
            {
                encoder.TrySend(input);
            }
            encoder.Dispose();
            Assert.Equal(held, allocator.AllocatedBytes);
            input.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void AllocationFailureReleasesTemporaryEncoderStorage()
        {
            MediaBufferAllocator allocator = new(16384);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            long held = allocator.AllocatedBytes;
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            using WritableMediaBuffer occupied = allocator.Allocate((int)(allocator.LimitBytes - held - 100));
            Assert.Throws<MediaMemoryLimitException>(() => encoder.TrySend(input));
            occupied.Dispose();
            Assert.Equal(held, allocator.AllocatedBytes);
            encoder.Reset();
            encoder.TrySend(input);
            using MediaPacket packet = encoder.Receive().Value!;
            encoder.Dispose();
            Assert.True(packet.Data.Length > 4);
            packet.Dispose();
            input.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TimingMustBeExactPositiveAndNonoverlapping()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            encoder.TrySend(first);
            using MediaPacket packet = encoder.Receive().Value!;
            Assert.Throws<InvalidDataException>(() => encoder.TrySend(first));
            encoder.Reset();
            using WritableMediaBuffer storage = allocator.Allocate(first.Data.Length);
            first.Data.Span.CopyTo(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            using VideoFrame inexact = new(format, buffer, first.Planes, new(1, new(1, 3000)), first.Duration);
            Assert.Throws<InvalidDataException>(() => encoder.TrySend(inexact));
            encoder.Reset();
            using VideoFrame missing = new(format, buffer, first.Planes, duration: first.Duration);
            Assert.Throws<NotSupportedException>(() => encoder.TrySend(missing));
            encoder.Reset();
            using VideoFrame zero = new(format, buffer, first.Planes, first.Timestamp, new(0, new(1, 1000)));
            Assert.Throws<NotSupportedException>(() => encoder.TrySend(zero));
            encoder.Reset();
            encoder.TrySend(first);
            encoder.CompleteInput();
            using MediaPacket drained = encoder.Receive().Value!;
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
            Assert.Throws<InvalidOperationException>(() => encoder.TrySend(first));
            Assert.Equal(packet.Data.ToArray(), drained.Data.ToArray());
        }

        [Theory]
        [InlineData(31, 32)]
        [InlineData(4096, 4096)]
        public void UnsupportedDimensionsFailBeforeRetainingStorage(int width, int height)
        {
            MediaBufferAllocator allocator = new(65536);
            Assert.Throws<NotSupportedException>(() => H264Encoder.Create(H264EncoderTests.Format(width, height), new(1, 1000), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1, 200)]
        [InlineData(200, 1)]
        public void ShortDisplayExtentsArePreservedAtValidPictureCadence(int firstDuration, int lastDuration)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            using VideoFrame first = input.WithTiming(new(0, new(1, 1000)), new(firstDuration, new(1, 1000)));
            using VideoFrame last = input.WithTiming(new(200, new(1, 1000)), new(lastDuration, new(1, 1000)));
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(first));
            using MediaPacket initial = encoder.Receive().Value!;
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(last));
            using MediaPacket final = encoder.Receive().Value!;
            Assert.Equal(first.Timestamp, initial.PresentationTime);
            Assert.Equal(first.Timestamp, initial.DecodeTime);
            Assert.Equal(first.Duration, initial.Duration);
            Assert.Equal(last.Timestamp, final.PresentationTime);
            Assert.Equal(last.Timestamp, final.DecodeTime);
            Assert.Equal(last.Duration, final.Duration);
            encoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
            initial.Dispose();
            final.Dispose();
            input.Dispose();
            first.Dispose();
            last.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(2, 2, 172, 1, true)]
        [InlineData(2, 2, 173, 1, false)]
        [InlineData(640, 576, 245760, 1440, true)]
        [InlineData(640, 576, 245760, 1439, false)]
        public void ConsecutivePicturesRespectTheLevelCadenceLimits(int width, int height, int ticksPerSecond,
            int delta, bool accepted)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(width, height);
            TimeBase clock = new(1, ticksPerSecond);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            using VideoFrame first = input.WithTiming(new(0, clock), new(1, clock));
            using VideoFrame next = input.WithTiming(new(delta, clock), new(1, clock));
            using H264Encoder encoder = H264Encoder.Create(format, clock, allocator,
                new() { KeyFrameInterval = TimeSpan.Zero });
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(first));
            using MediaPacket initial = encoder.Receive().Value!;
            initial.Dispose();
            if (accepted)
            {
                Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(next));
                using MediaPacket packet = encoder.Receive().Value!;
                Assert.Equal(next.Timestamp, packet.PresentationTime);
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => encoder.TrySend(next));
                Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            }
            encoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(first));
            using MediaPacket replay = encoder.Receive().Value!;
            encoder.Dispose();
            replay.Dispose();
            input.Dispose();
            first.Dispose();
            next.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void LargeTimestampGapsAndClockNumeratorsPreserveCadenceValidation()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(2, 2);
            TimeBase clock = new(int.MaxValue, int.MaxValue - 1);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            using H264Encoder encoder = H264Encoder.Create(format, clock, allocator);
            long[] timestamps = [long.MinValue, long.MaxValue - 1];
            foreach (long timestamp in timestamps)
            {
                using VideoFrame frame = input.WithTiming(new(timestamp, clock), new(1, clock));
                Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
                using MediaPacket packet = encoder.Receive().Value!;
                Assert.Equal(frame.Timestamp, packet.PresentationTime);
                Assert.Equal(frame.Duration, packet.Duration);
            }
            encoder.Dispose();
            input.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
