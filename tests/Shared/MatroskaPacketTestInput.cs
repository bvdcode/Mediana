// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaPacketTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPacketTestInput
    {
        public static async Task VerifyTimingAndOrderAsync()
        {
            byte[] payload = [255, .. Enumerable.Range(0, 256).Select(value => (byte)value)];
            byte[] data = Cluster(Unsigned(0xE7, 5), Block(timestamp: -1, flags: 0x84, payload: payload), Block(payload: [0xED]));
            MatroskaTrack track = Track() with { Timing = new(0.5, 23219955, 7000000, 0) };
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator, [track]))
            {
                long retained = allocator.AllocatedBytes;
                using (MatroskaPacketSource packets = new(source, allocator, index, [track], [AacStream(rate: 44100)], 1000000, Options))
                {
                    long baseline = allocator.AllocatedBytes;
                    int reads = source.ReadCalls;
                    MediaTimestamp target = new(-2500000 + (19 * 1024L * 1000000000 + 22050) / 44100, MatroskaTrackClock.TimeBase);
                    Require(packets.FindSeekPoint(0, target, 256)?.PacketIndex == 19);
                    Require(packets.FindSeekPoint(0, new(-100, new(1, 1)), 257)?.PacketIndex == 0);
                    Require(packets.FindSeekPoint(0, new(0, new(1, 1)), 257)?.PacketIndex == 256);
                    Require(packets.FindSeekPoint(0, target, 0) is null);
                    Require(source.ReadCalls == reads && allocator.AllocatedBytes == baseline);
                    using MediaPacket first = (await packets.ReadPacketAsync())!;
                    using MediaPacket lease = first.Retain();
                    Require(first.Data.Span[0] == 0 && first.PresentationTime == new MediaTimestamp(-2500000, MatroskaTrackClock.TimeBase));
                    Require(allocator.AllocatedBytes == baseline + 1 && first.DecodeTime is null && first.IsKeyFrame == true);
                    first.Dispose();
                    for (int frame = 255; frame >= 0; frame--)
                    {
                        using MediaPacket packet = await packets.ReadPacketAtAsync(0, frame);
                        long ns = -2500000 + (frame * 1024L * 1000000000 + 22050) / 44100;
                        Require(packet.Data.Span[0] == frame && packet.PresentationTime == new MediaTimestamp(ns, MatroskaTrackClock.TimeBase));
                        Require(packet.Duration == AacStream(rate: 44100).FrameDuration && packet.DecodeTime is null);
                    }
                    for (int frame = 1; frame < 256; frame++)
                    {
                        using MediaPacket packet = (await packets.ReadPacketAsync())!;
                        Require(packet.Data.Span[0] == frame);
                    }
                    using MediaPacket last = (await packets.ReadPacketAsync())!;
                    Require(last.Data.Span[0] == 0xED && await packets.ReadPacketAsync() is null);
                    packets.Dispose();
                    index.Dispose();
                    Require(lease.Data.Span[0] == 0 && last.Data.Span[0] == 0xED && allocator.AllocatedBytes == 2);
                    last.Dispose();
                    lease.Dispose();
                    Require(allocator.AllocatedBytes == 0 && !source.IsDisposed && baseline > retained);
                }
            }
        }

        public static async Task VerifyUnknownTimesAsync()
        {
            byte[] data = Cluster(Unsigned(0xE7, 3), Field(0xA0,
                [.. Block(0xA1, timestamp: -4, flags: 4, payload: [1, 0xAA, 0xBB]), .. Unsigned(0x9B, 9), .. Signed(0xFB, -1)]),
                Field(0xA0, [.. Block(0xA1, timestamp: 1, flags: 0), .. Unsigned(0x9B, 7)]),
                Block(timestamp: 2, flags: 4, payload: [1, 0xCC, 0xDD]));
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data);
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator))
            using (MatroskaPacketSource packets = new(source, allocator, index, [Track()], [AacStream() with { FrameDuration = null }], 1000000, Options))
            {
                using MediaPacket first = (await packets.ReadPacketAsync())!;
                using MediaPacket second = (await packets.ReadPacketAsync())!;
                using MediaPacket third = (await packets.ReadPacketAsync())!;
                Require(first.PresentationTime == new MediaTimestamp(-1000000, MatroskaTrackClock.TimeBase));
                Require(first.Duration is null && second.Duration is null && second.PresentationTime is null && first.IsKeyFrame == false);
                Require(third.Duration == new MediaDuration(7000000, MatroskaTrackClock.TimeBase) && third.IsKeyFrame == true);
                Require(first.DecodeTime is null && third.DecodeTime is null);
            }
            Require(allocator.AllocatedBytes == 0);
            MatroskaTrack declared = Track() with { Timing = new(1, 3000000, 0, 0) };
            byte[] simple = Cluster(Unsigned(0xE7, 3), Block(flags: 4, payload: [1, 0xCC, 0xDD]));
            await using EbmlElementTestSource defaultSource = new(simple);
            using (MatroskaPacketIndex index = await BuildAsync(defaultSource, allocator, [declared]))
            using (MatroskaPacketSource packets = new(defaultSource, allocator, index, [declared], [AacStream() with { FrameDuration = null }], 1000000, Options))
            {
                using MediaPacket second = await packets.ReadPacketAtAsync(0, 1);
                Require(second.PresentationTime == new MediaTimestamp(6000000, MatroskaTrackClock.TimeBase));
                Require(second.Duration == new MediaDuration(3000000, MatroskaTrackClock.TimeBase));
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyUnsupportedSemanticsAsync()
        {
            foreach (byte[] block in new[]
            {
                Block(flags: 0x88), Field(0xA0, [.. Block(0xA1, flags: 0), .. Field(0xA4, [1])]),
                Field(0xA0, [.. Block(0xA1, flags: 0), .. Field(0x75A1, [])]),
                Field(0xA0, [.. Block(0xA1, flags: 0), .. Signed(0x75A2, 1)]),
                Field(0xA0, [.. Block(0xA1, flags: 0), .. Signed(0x75A2, -1)])
            })
            {
                byte[] data = Cluster(Unsigned(0xE7, 0), block);
                MediaBufferAllocator allocator = new(131072);
                await using EbmlElementTestSource source = new(data);
                using (MatroskaPacketIndex index = await BuildAsync(source, allocator))
                using (MatroskaPacketSource packets = new(source, allocator, index, [Track()], [AacStream()], 1000000, Options))
                {
                    long before = allocator.AllocatedBytes;
                    await RejectAsync<NotSupportedException>(async () => { using MediaPacket? packet = await packets.ReadPacketAsync(); });
                    Require(allocator.AllocatedBytes == before);
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
        }

        public static async Task VerifyCancellationAndRetryAsync()
        {
            byte[] data = Cluster(Unsigned(0xE7, 0), Block(payload: new byte[512]), Block(payload: [0xCD]));
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource indexedSource = new(data);
            using (MatroskaPacketIndex index = await BuildAsync(indexedSource, allocator))
            {
                TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                long payloadOffset;
                using (MatroskaBlockReader blocks = new(indexedSource, allocator, 4096, 1024))
                {
                    await blocks.ReadAsync(index.GetBlock(0).Element);
                    payloadOffset = blocks.GetFrame(0).Offset;
                }
                await using EbmlElementTestSource source = new(data) { ReadGate = gate.Task, ReadGateOffset = payloadOffset };
                using MatroskaPacketSource packets = new(source, allocator, index, [Track()], [AacStream()], 1000000, Options);
                long before = allocator.AllocatedBytes;
                using CancellationTokenSource cancellation = new();
                Task<MediaPacket?> pending = packets.ReadPacketAsync(cancellation.Token).AsTask();
                Require(!pending.IsCompleted && allocator.AllocatedBytes == before + 512);
                await RejectAsync<InvalidOperationException>(async () => { await packets.ReadPacketAtAsync(0, 0); });
                await RejectAsync<InvalidOperationException>(() => { packets.Dispose(); return Task.CompletedTask; });
                await cancellation.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => { using MediaPacket? packet = await pending; });
                Require(allocator.AllocatedBytes == before && !source.IsDisposed);
                gate.SetResult();
                using MediaPacket first = (await packets.ReadPacketAsync())!;
                Require(first.Data.Length == 512);
                source.ReadBoundary = payloadOffset;
                await RejectAsync<InvalidOperationException>(async () => { using MediaPacket? packet = await packets.ReadPacketAsync(); });
                source.ReadBoundary = long.MaxValue;
                using MediaPacket second = (await packets.ReadPacketAsync())!;
                Require(second.Data.Span[0] == 0xCD && await packets.ReadPacketAsync() is null);
                await RejectAsync<OperationCanceledException>(async () => { await packets.ReadPacketAsync(new CancellationToken(true)); });
                await RejectAsync<ArgumentOutOfRangeException>(async () => { await packets.ReadPacketAtAsync(0, 2); });
                packets.Dispose();
                await RejectAsync<ObjectDisposedException>(async () => { await packets.ReadPacketAsync(); });
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyQuotaAndEmptyAsync()
        {
            byte[] data = Cluster(Unsigned(0xE7, 0), Block(payload: new byte[8192]), Block(payload: []));
            await using EbmlElementTestSource source = new(data);
            MediaBufferAllocator indexing = new(131072);
            using MatroskaPacketIndex index = await BuildAsync(source, indexing);
            MediaBufferAllocator exact = new(3328 + 8192);
            using (MatroskaPacketSource packets = new(source, exact, index, [Track()], [AacStream()], 1000000, Options))
            {
                using MediaPacket packet = (await packets.ReadPacketAsync())!;
                Require(exact.AllocatedBytes == exact.LimitBytes);
                await RejectAsync<MediaMemoryLimitException>(async () => { await packets.ReadPacketAsync(); });
                packet.Dispose();
                using MediaPacket empty = (await packets.ReadPacketAsync())!;
                Require(empty.Data.IsEmpty && exact.AllocatedBytes == 3329);
            }
            Require(exact.AllocatedBytes == 0 && !source.IsDisposed);
            MediaBufferAllocator insufficient = new(3328 + 8191);
            using (MatroskaPacketSource packets = new(source, insufficient, index, [Track()], [AacStream()], 1000000, Options))
            {
                await RejectAsync<MediaMemoryLimitException>(async () => { await packets.ReadPacketAsync(); });
                Require(insufficient.AllocatedBytes == 3328);
            }
            Require(insufficient.AllocatedBytes == 0);
            index.Dispose();
            Require(indexing.AllocatedBytes == 0);
        }
    }
}
