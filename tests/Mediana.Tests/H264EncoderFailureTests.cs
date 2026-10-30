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

        [Fact]
        public void SmallPicturesStillRespectTheLevelMaximumFrameRate()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(2, 2);
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 0);
            using WritableMediaBuffer storage = allocator.Allocate(input.Data.Length);
            input.Data.Span.CopyTo(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            using VideoFrame fast = new(format, buffer, input.Planes, input.Timestamp, new(1, new(1, 1000)));
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            Assert.Throws<NotSupportedException>(() => encoder.TrySend(fast));
            Assert.True(H264EncoderConfiguration.ExceedsAccessUnitLimit(1000000, 1));
            Assert.False(H264EncoderConfiguration.ExceedsAccessUnitLimit(1024, 1));
        }
    }
}
