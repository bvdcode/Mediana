// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    internal static class EncodingGroupTestInput
    {
        public static async Task VerifyAsync()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder first = new(allocator);
            using BufferedVideoEncoder second = new(allocator);
            await using CollectingPacketWriter left = new();
            await using CollectingPacketWriter right = new();
            MediaEncodePipeline<VideoFrame> leftPipeline = new(first, left);
            MediaEncodePipeline<VideoFrame> rightPipeline = new(second, right);
            MediaEncodePipeline<VideoFrame>[] targets = [leftPipeline, rightPipeline];
            MediaEncodeGroup<VideoFrame> group = new(targets);
            targets[1] = leftPipeline;
            for (int index = 1; index <= 3; index++)
            {
                using VideoFrame frame = FrameFixture.Create(allocator, (byte)(index * 10), index);
                await group.WriteAsync(frame);
                if (frame.Data.Span[0] != index * 10)
                {
                    throw new InvalidDataException("An encoding target disposed or changed a borrowed frame.");
                }
            }
            await group.CompleteInputAsync();
            byte[] expected = [10, 11, 20, 21, 30, 31];
            if (!left.Packets.SelectMany(packet => packet).SequenceEqual(expected)
                || !right.Packets.SelectMany(packet => packet).SequenceEqual(expected)
                || group.FramesSubmitted != 3 || leftPipeline.FramesSubmitted != 3 || rightPipeline.FramesSubmitted != 3
                || left.IsComplete || right.IsComplete || left.IsDisposed || right.IsDisposed
                || first.IsDisposed || second.IsDisposed || allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The encoding group lost delayed packets or changed borrowed component ownership.");
            }
        }
    }
}
