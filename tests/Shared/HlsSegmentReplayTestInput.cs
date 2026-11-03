// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal static class HlsSegmentReplayTestInput
    {
        public static async Task VerifyAsync(IReadOnlyList<EncodedStreamInfo> streams,
            IReadOnlyList<(int Stream, byte[] Data, long Pts, long Dts)> packets,
            IReadOnlyList<int> starts, IReadOnlyList<MpegTsSegmentState?> states,
            IReadOnlyList<MemoryStream> originals, MediaBufferAllocator allocator)
        {
            if (states.Count < 2 || states.Count != starts.Count || states.Count != originals.Count ||
                states[0] is not null || starts[0] != 0 || states.Skip(1).Any(state => state is null))
            {
                throw new InvalidDataException("HLS factories did not capture one preceding state per actual segment.");
            }
            foreach (int number in new[] { states.Count - 1, 0, 1, states.Count - 1 })
            {
                using MemoryStream destination = new();
                await using (MpegTsWriter writer = states[number] is MpegTsSegmentState state
                    ? MpegTsWriter.ResumeSegment(destination, state, allocator)
                    : MpegTsWriter.Create(destination, streams, allocator))
                {
                    int end = number + 1 == starts.Count ? packets.Count : starts[number + 1];
                    for (int index = starts[number]; index < end; index++)
                    {
                        (int stream, byte[] data, long pts, long dts) = packets[index];
                        using WritableMediaBuffer storage = allocator.Allocate(data.Length);
                        data.CopyTo(storage.Span);
                        using MediaBuffer buffer = storage.Publish();
                        using MediaPacket packet = new(stream, buffer, new(pts - 90000, new(1, 90000)), new(dts - 90000, new(1, 90000)));
                        await writer.WritePacketAsync(packet);
                    }
                    await writer.CompleteAsync();
                }
                if (!destination.CanWrite || !destination.ToArray().AsSpan().SequenceEqual(originals[number].ToArray()))
                {
                    throw new InvalidDataException("A repeated out-of-order HLS request changed transport bytes or borrowed stream ownership.");
                }
            }
        }
    }
}
