// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class EncodingGroupTests
    {
        [Fact]
        public Task FanOutRetainsBorrowedFramesAndDrainsEveryTarget() => EncodingGroupTestInput.VerifyAsync();

        [Fact]
        public async Task FailureAfterPartialSubmissionMakesTheWholeGroupTerminal()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder first = new(allocator);
            using BufferedVideoEncoder second = new(allocator);
            await using CollectingPacketWriter left = new();
            await using CollectingPacketWriter right = new() { FailWrites = true };
            MediaEncodePipeline<VideoFrame> a = new(first, left);
            MediaEncodePipeline<VideoFrame> b = new(second, right);
            MediaEncodeGroup<VideoFrame> group = new([a, b]);
            using VideoFrame frame = FrameFixture.Create(allocator, 10, 0);
            await group.WriteAsync(frame);
            await Assert.ThrowsAsync<IOException>(async () => await group.WriteAsync(frame));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await group.WriteAsync(frame));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await group.CompleteInputAsync());
            Assert.Equal(1, group.FramesSubmitted);
            Assert.False(first.IsDisposed);
            Assert.False(second.IsDisposed);
            first.Dispose();
            second.Dispose();
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationIsTerminalAndDoesNotDisposeBorrowedComponents()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder encoder = new(allocator);
            await using CollectingPacketWriter writer = new();
            MediaEncodePipeline<VideoFrame> pipeline = new(encoder, writer);
            MediaEncodeGroup<VideoFrame> group = new([pipeline]);
            using VideoFrame frame = FrameFixture.Create(allocator, 10, 0);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await group.WriteAsync(frame, cancellation.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await group.WriteAsync(frame));
            Assert.Equal(0, pipeline.FramesSubmitted);
            Assert.Equal(0, group.FramesSubmitted);
            Assert.False(encoder.IsDisposed);
            Assert.False(writer.IsDisposed);
            Assert.Equal(10, frame.Data.Span[0]);
        }

        [Fact]
        public void InvalidTargetsFailBeforeEncoding()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder encoder = new(allocator);
            CollectingPacketWriter writer = new();
            MediaEncodePipeline<VideoFrame> pipeline = new(encoder, writer);
            Assert.Throws<ArgumentException>(() => new MediaEncodeGroup<VideoFrame>([]));
            Assert.Throws<ArgumentException>(() => new MediaEncodeGroup<VideoFrame>([pipeline, pipeline]));
            Assert.Throws<ArgumentException>(() => new MediaEncodeGroup<VideoFrame>([null!]));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
