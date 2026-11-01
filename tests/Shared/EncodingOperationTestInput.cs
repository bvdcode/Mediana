// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    internal static class EncodingOperationTestInput
    {
        internal static async Task VerifyAsync()
        {
            MediaBufferAllocator allocator = new(4096);
            using BufferedVideoEncoder encoder = new(allocator);
            await using CollectingPacketWriter writer = new();
            List<MediaOperationProgress> updates = [];
            int index = 0;
            ValueTask<VideoFrame?> Read(CancellationToken token) => ValueTask.FromResult<VideoFrame?>(index < 3
                ? FrameFixture.Create(allocator, (byte)(10 * ++index), index, new MediaDuration(index, new(1, 1000))) : null);
            MediaOperationProgress result = await MediaOperations.EncodeVideoAsync(Read, encoder, writer, updates.Add);
            byte[] expected = [10, 11, 20, 21, 30, 31];
            if (!writer.Packets.SelectMany(packet => packet).SequenceEqual(expected) ||
                result.Frames != 3 || result.Packets != 6 || result.PayloadBytes != 6 || !result.IsComplete ||
                result.ProcessedDuration != new MediaDuration(6, new(1, 1000)) ||
                !updates.Select(update => update.ProcessedDuration).SequenceEqual(new MediaDuration?[]
                {
                    new(1, new(1, 1000)), new(3, new(1, 1000)), new(6, new(1, 1000)), new(6, new(1, 1000))
                }) ||
                !writer.IsComplete || writer.IsDisposed || encoder.IsDisposed || allocator.AllocatedBytes != 0 || updates[^1] != result)
            {
                throw new InvalidOperationException("Encoding did not preserve delayed output, packet counts, component or frame ownership.");
            }
        }
    }
}
