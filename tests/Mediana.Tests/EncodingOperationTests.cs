// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class EncodingOperationTests
    {
        [Fact]
        public async Task OperationDrainsMultiplePacketsAndDelayedTailAndBorrowsComponents()
        {
            await EncodingOperationTestInput.VerifyAsync();
        }

        [Fact]
        public async Task InvalidDrainingStateDoesNotFinalizeOutput()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder encoder = new(allocator, invalidDrain: true);
            await using CollectingPacketWriter writer = new();
            await Assert.ThrowsAsync<InvalidOperationException>(() => MediaOperations.EncodeVideoAsync(_ => ValueTask.FromResult<VideoFrame?>(null), encoder, writer));
            Assert.False(writer.IsComplete);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailureAndCancellationLeaveOutputUnfinishedAndReleaseFrameOwnership(bool cancel)
        {
            MediaBufferAllocator allocator = new(4096);
            BufferedVideoEncoder encoder = new(allocator);
            await using CollectingPacketWriter writer = new() { FailWrites = !cancel };
            int index = 0;
            using CancellationTokenSource cancellation = new();
            async ValueTask<VideoFrame?> Read(CancellationToken token)
            {
                if (cancel)
                {
                    await cancellation.CancelAsync();
                }
                return index < 2 ? FrameFixture.Create(allocator, (byte)++index, index) : null;
            }
            Task<MediaOperationProgress> operation = MediaOperations.EncodeVideoAsync(Read, encoder, writer, cancellationToken: cancellation.Token);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => operation);
            }
            Assert.False(writer.IsComplete);
            encoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void OutputRegistryIsExplicitAndBuildProducesAnIndependentSnapshot()
        {
            MediaEngineBuilder builder = new();
            MediaEngine empty = builder.Build();
            builder.AddVideoEncoder(new H264EncoderFactory()).AddContainerWriter(new MpegTsWriterFactory());
            Assert.Throws<ArgumentException>(() => builder.AddVideoEncoder(new H264EncoderFactory()));
            Assert.Throws<ArgumentException>(() => builder.AddContainerWriter(new MpegTsWriterFactory()));
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            Assert.Throws<NotSupportedException>(() => empty.CreateVideoEncoder("h264", format, new(1, 1000), allocator));
            using Codecs.IMediaEncoder<VideoFrame> encoder = builder.Build().CreateVideoEncoder("h264", format, new(1, 1000), allocator);
            Assert.Equal("h264", encoder.Stream.CodecId);
        }
    }
}
