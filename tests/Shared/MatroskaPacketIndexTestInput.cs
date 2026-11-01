// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Formats;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPacketIndexTestInput
    {
        public static async Task VerifyManyPacketsAsync()
        {
            byte[] numbers = [9, 3, 71];
            List<MatroskaPacketAddress>[] expected = [[], [], []];
            List<byte[]> fields = [];
            for (int block = 0; block < 1500; block++)
            {
                int stream = (block * 7 + block / 17) % numbers.Length;
                int frames = block % 8 + 1;
                byte[] payload = frames == 1 ? [0x7A] : [(byte)(frames - 1), .. Enumerable.Repeat((byte)0x7A, frames)];
                byte flags = frames == 1 ? (byte)0x80 : (byte)0x84;
                fields.Add(Block(track: numbers[stream], timestamp: (short)block, flags: flags, payload: payload));
                for (int frame = 0; frame < frames; frame++)
                {
                    expected[stream].Add(new(block, frame));
                }
            }
            fields.Add(Unsigned(0xE7, 0));
            byte[] data = Cluster([.. fields]);
            MediaBufferAllocator allocator = new(524288);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            MatroskaTrack[] tracks = numbers.Select(number => Track(number)).ToArray();
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator, tracks,
                Options with { MaximumBlocks = 2048, MaximumIndexBytes = 262144 }))
            {
                Require(index.BlockCount == 1500);
                for (int stream = 0; stream < numbers.Length; stream++)
                {
                    Require(index.GetPacketCount(stream) == expected[stream].Count && index.GetPayloadBytes(stream) == expected[stream].Count);
                    for (int packet = expected[stream].Count - 1; packet >= 0; packet--)
                    {
                        Require(index.Locate(stream, packet) == expected[stream][packet]);
                    }
                }
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyTrackOrderAsync()
        {
            byte[] lace = [255, .. Enumerable.Range(0, 256).Select(value => (byte)value)];
            byte[] group = Field(0xA0, [.. Unsigned(0x9B, 64), .. Signed(0xFB, -1), .. Signed(0x75A2, -7),
                .. Block(0xA1, 5, short.MaxValue, 0, [0xC1, 0xC2])]);
            byte[] data = [.. Cluster(Block(track: 2, timestamp: short.MinValue), Block(track: 5, flags: 0x84, payload: lace),
                Block(track: 2, timestamp: 37), Unsigned(0xE7, ulong.MaxValue)), .. Cluster(Unsigned(0xE7, 0), group)];
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            MatroskaPacketIndex index = await BuildAsync(source, allocator, [Track(5), Track(2), Track(7)]);
            using (index)
            {
                Require(allocator.AllocatedBytes > 0 && allocator.AllocatedBytes < 2048);
                Require(index.BlockCount == 4 && index.ClusterCount == 2 && index.TrackCount == 3);
                Require(index.GetClusterTimestamp(0) == ulong.MaxValue && index.GetClusterTimestamp(1) == 0);
                Require(index.GetPacketCount(0) == 257 && index.GetPacketCount(1) == 2 && index.GetPacketCount(2) == 0);
                Require(index.GetPayloadBytes(0) == 258 && index.GetPayloadBytes(1) == 2 && index.GetPayloadBytes(2) == 0);
                Require(index.GetBlock(0) is { StreamIndex: 1, ClusterIndex: 0, RelativeTimestamp: short.MinValue, IsSimpleBlock: true, Flags: 0x80 });
                Require(index.GetBlock(1) is { StreamIndex: 0, FrameCount: 256, ClusterIndex: 0 });
                Require(index.GetBlock(2) is { StreamIndex: 1, ClusterIndex: 0, RelativeTimestamp: 37 });
                Require(index.GetBlock(3) is { StreamIndex: 0, ClusterIndex: 1, RelativeTimestamp: short.MaxValue, HasReferences: true, IsSimpleBlock: false });
                for (int packet = 255; packet >= 0; packet--)
                {
                    Require(index.Locate(0, packet) == new MatroskaPacketAddress(1, packet));
                }
                Require(index.Locate(0, 256) == new MatroskaPacketAddress(3, 0));
                Require(index.Locate(1, 0) == new MatroskaPacketAddress(0, 0) && index.Locate(1, 1) == new MatroskaPacketAddress(2, 0));
                Require(index.GetExtensions(0) is null && index.GetExtensions(1) is null);
                Require(index.GetExtensions(3) is { DurationTicks: 64, DiscardPaddingNanoseconds: -7 });
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => index.Locate(0, -1)));
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => index.Locate(0, 257)));
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => index.Locate(2, 0)));
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => index.GetPacketCount(3)));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            index.Dispose();
            await RejectAsync<ObjectDisposedException>(() => Task.Run(() => index.GetBlock(0)));
        }

        public static async Task VerifyInvalidAsync()
        {
            byte[] timestamp = Unsigned(0xE7, 0);
            byte[] block = Block();
            await RejectBuildAsync<InvalidDataException>(Cluster(block));
            await RejectBuildAsync<InvalidDataException>(Cluster(timestamp, block, timestamp));
            await RejectBuildAsync<InvalidDataException>(Cluster(timestamp, Block(track: 2)));
            MatroskaTrack noLace = Track() with { Flags = new(true, true, false, false) };
            await RejectBuildAsync<InvalidDataException>(Cluster(timestamp, Block(flags: 0x84, payload: [1, 2, 3])), [noLace]);
            MatroskaTrack fixedDuration = Track() with { Timing = new(1, 1000000, 0, 0) };
            await RejectBuildAsync<InvalidDataException>(Cluster(timestamp, Field(0xA0, Block(0xA1, flags: 0))), [fixedDuration]);
            await RejectBuildAsync<InvalidDataException>(Cluster(timestamp, Field(0xA0, [])));
            await RejectBuildAsync<NotSupportedException>(Cluster(timestamp, block, block), options: Options with { MaximumBlocks = 1 });
            await RejectBuildAsync<NotSupportedException>(Cluster(timestamp, Block(flags: 0x84, payload: [1, 2, 3])),
                options: Options with { MaximumPacketsPerTrack = 1 });
            await RejectBuildAsync<NotSupportedException>(Cluster(timestamp, block), options: Options with { MaximumIndexBytes = 1 });
            await RejectBuildAsync<MediaMemoryLimitException>(Cluster(timestamp, block), budget: ScratchBytes);
            await RejectBuildAsync<MediaMemoryLimitException>(Cluster(timestamp, block), budget: ScratchBytes - 1);
            byte[] truncated = Cluster(timestamp, block);
            await RejectBuildAsync<InvalidDataException>(truncated[..^1]);
            MediaBufferAllocator allocator = new(8192);
            using (MatroskaPacketIndex index = new(allocator, [Track()], Options))
            {
                await RejectAsync<InvalidOperationException>(() => Task.Run(() => index.Locate(0, 0)));
                index.Complete();
                Require(index.BlockCount == 0 && index.GetPacketCount(0) == 0);
                await RejectAsync<InvalidOperationException>(() => Task.Run(() => index.AddCluster(0)));
            }
            await RejectAsync<ArgumentException>(() => Task.Run(() => new MatroskaPacketIndex(allocator, [Track(), Track()], Options)));
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyChecksumsAsync()
        {
            byte[] payload = [.. Unsigned(0xE7, 0), .. Block()];
            byte[] crc = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crc, IeeeCrc32.Compute(payload));
            byte[] valid = Cluster(Field(0xBF, crc), payload);
            MediaBufferAllocator allocator = new(16384);
            await using (EbmlElementTestSource source = new(valid))
            {
                using MatroskaPacketIndex index = await BuildAsync(source, allocator);
                Require(index.BlockCount == 1);
            }
            Require(allocator.AllocatedBytes == 0);
            crc[0] ^= 0x80;
            byte[] invalid = Cluster(Field(0xBF, crc), payload);
            await RejectBuildAsync<InvalidDataException>(invalid);
            await using (EbmlElementTestSource source = new(invalid))
            {
                using MatroskaPacketIndex index = await BuildAsync(source, allocator, options: Options with { VerifyClusterChecksums = false });
                Require(index.BlockCount == 1);
            }
            await RejectBuildAsync<InvalidDataException>(Cluster(payload, Field(0xBF, crc)),
                options: Options with { VerifyClusterChecksums = false });
            await RejectBuildAsync<InvalidDataException>(Cluster(Field(0xBF, crc[..3]), payload),
                options: Options with { VerifyClusterChecksums = false });
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyUnknownClustersAndExactQuotaAsync()
        {
            byte[] data = [0x1F, 0x43, 0xB6, 0x75, 0xFF, .. Block(), .. Field(0xE7, []),
                0x1F, 0x43, 0xB6, 0x75, 0xFF, .. Block(timestamp: -3), .. Unsigned(0xE7, 99)];
            MediaBufferAllocator allocator = new(65536);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 1 };
            long retained;
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator))
            {
                retained = allocator.AllocatedBytes;
                Require(index.ClusterCount == 2 && index.BlockCount == 2 && index.GetClusterTimestamp(0) == 0 && index.GetClusterTimestamp(1) == 99);
                Require(index.GetBlock(0).ClusterIndex == 0 && index.GetBlock(1).ClusterIndex == 1);
            }
            Require(allocator.AllocatedBytes == 0);
            MediaBufferAllocator exact = new(ScratchBytes + retained);
            using (MatroskaPacketIndex index = await BuildAsync(source, exact, options: Options with { MaximumIndexBytes = (int)retained }))
            {
                Require(index.GetPacketCount(0) == 2 && exact.AllocatedBytes == retained);
            }
            Require(exact.AllocatedBytes == 0 && !source.IsDisposed);
            int minimum = 2 * (Unsafe.SizeOf<MatroskaIndexedBlock>() + Unsafe.SizeOf<MatroskaTrackBlock>() + sizeof(ulong));
            await RejectBuildAsync<NotSupportedException>(data, options: Options with { MaximumIndexBytes = minimum - 1 });
            await RejectBuildAsync<MediaMemoryLimitException>(data, budget: ScratchBytes + retained - 1);
        }

        public static async Task VerifyCancellationAsync()
        {
            byte[] data = Cluster(Unsigned(0xE7, 0), Block(), Block());
            MediaBufferAllocator allocator = new(65536);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(data) { ReadGate = gate.Task, ReadGateOffset = data.Length - 8, MaximumRead = 3 };
            using CancellationTokenSource cancellation = new();
            Task<MatroskaPacketIndex> pending = BuildAsync(source, allocator, token: cancellation.Token).AsTask();
            Require(!pending.IsCompleted && allocator.AllocatedBytes > ScratchBytes);
            await cancellation.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await pending);
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            int calls = source.ReadCalls;
            await RejectAsync<OperationCanceledException>(async () => await BuildAsync(source, allocator, token: cancellation.Token));
            Require(allocator.AllocatedBytes == 0 && source.ReadCalls == calls);
            gate.SetResult();
            using (MatroskaPacketIndex retry = await BuildAsync(source, allocator))
            {
                Require(retry.BlockCount == 2 && retry.GetPacketCount(0) == 2);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }
    }
}
