// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    internal static class PacketMergeTestInput
    {
        public static async Task VerifyAsync()
        {
            MediaBufferAllocator allocator = new(2);
            await using TimedPacketSource video = new(allocator, 0, new(1, 24), [0, 1, 2, 3]);
            await using TimedPacketSource audio = new(allocator, 1, new(1, 48000), [-1024, 0, 1024, 2048, 3072, 4096, 5120]);
            await using CollectingPacketWriter writer = new();
            long packets = await MediaPacketMerge.WriteAsync([video, audio], writer);
            int[] expected = [1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0];
            if (packets != expected.Length || !writer.Streams.SequenceEqual(expected)
                || !writer.IsComplete || writer.IsDisposed || video.IsDisposed || audio.IsDisposed
                || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Packet merging changed rational decode order, bounded storage or borrowed component ownership.");
            }
        }
    }
}
