// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    internal static class MatroskaReaderTestInput
    {
        public static async Task VerifyMetadataAndOwnershipAsync()
        {
            byte[] info = Field(0x1549A966, [.. Field(0x5741, "writer"u8), .. Float(0x4489, 1.5),
                .. Field(0x7BA9, "Title"u8), .. Unsigned(0x2AD7B1, 1), .. Signed(0x4461, -150), .. Field(0x4D80, "muxer"u8)]);
            byte[] data = Document(info, Tracks, Cluster(Unsigned(0xE7, 0), Block(track: 5, payload: [1, 2, 3])));
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 1 };
            MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            Require(reader.Info.FormatId == "matroska" && reader.Info.DeclaredDuration == new MediaDuration(2, MatroskaTrackClock.TimeBase));
            Require(reader.SegmentInfo.Duration == 1.5 && reader.SegmentInfo.DateNanoseconds == -150);
            Require(reader.Info.Dates![0].Value == new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-2));
            Require(reader.Info.Tags!.Select(tag => tag.Name).SequenceEqual(new[] { "WritingApp", "Title", "MuxingApp" }));
            Require(reader.Info.Tags!.All(tag => tag.Namespace == "matroska" && tag.SourcePath == "Segment/Info"));
            MatroskaTrackInfo track = reader.Tracks[0];
            Require(track.Number == 5 && track.Uid == 6 && track.Type == 2 && track.CodecId == "A_UNKNOWN");
            Require(ReferenceEquals(track.Stream, reader.Streams[0]) && track.Stream.StreamIndex == 0);
            Require(reader.Info.StreamInfo![0].Language == "ru-RU" && reader.Info.StreamInfo[0].Tags![0].Values[0] == "Sound");
            Require(reader.Info.StreamInfo[0].PacketIndex is { PacketCount: 1, PayloadBytes: 3, DecodeDuration: null, AveragePayloadBitRate: null });
            Require(reader.GetPacketCount(0) == 1 && track.Stream.Format == MatroskaStreamFormats.StoredFrames);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            Require(packet.DecodeTime is null && packet.PresentationTime == new MediaTimestamp(0, MatroskaTrackClock.TimeBase));
            using MediaPacket sequential = (await reader.ReadPacketAsync())!;
            Require(packet.Data.Span.SequenceEqual(sequential.Data.Span) && await reader.ReadPacketAsync() is null);
            await reader.DisposeAsync();
            await reader.DisposeAsync();
            Require(!source.IsDisposed && allocator.AllocatedBytes == 6);
            data.AsSpan().Clear();
            Require(packet.Data.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
            Require(track.Stream.Configuration.Span.SequenceEqual(new byte[] { 10, 20 }) && reader.Info.Tags![1].Values[0] == "Title");
            await RejectAsync<ObjectDisposedException>(async () => await reader.ReadPacketAsync());
            await RejectAsync<ObjectDisposedException>(() => { reader.GetPacketCount(0); return Task.CompletedTask; });
            sequential.Dispose();
            packet.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyRecurringAndMissingMetadataAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[] tracks = Tracks;
            await using EbmlElementTestSource source = new(Document(info, tracks, info, tracks));
            MediaBufferAllocator allocator = new(131072);
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Require(reader.Info.Tags!.Count == 2 && reader.Tracks.Count == 1 && reader.Info.DeclaredDuration is null);
                Require(reader.Info.StreamInfo![0].PacketIndex!.DecodeDuration == new MediaDuration(0, MatroskaTrackClock.TimeBase));
            }
            Require(allocator.AllocatedBytes == 0);
            await RejectOpenAsync<InvalidDataException>(Document(tracks));
            await RejectOpenAsync<InvalidDataException>(Document(info));
            await RejectOpenAsync<NotSupportedException>(Document(info, tracks, Field(0x1549A966,
                [.. Field(0x5741, "bb"u8), .. Field(0x4D80, "a"u8)])));
            await RejectOpenAsync<NotSupportedException>(Document(info, tracks,
                Field(0x1654AE6B, NativeTrack(6))));
        }

        public static async Task VerifyLimitsAndTraversalAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[] data = Document(info, Tracks, Cluster(Unsigned(0xE7, 0), Block(track: 5)));
            foreach (MatroskaReadOptions options in new[]
            {
                new MatroskaReadOptions { MaxMetadataBytes = InfoFields.Length }, new() { MaxElements = 1 },
                new() { MaxIndexBytes = 1 }, new() { MaxCodecConfigurationBytes = 1 }, new() { MaxTotalCodecConfigurationBytes = 1 },
                new() { MaxTextBytes = 2 }
            })
            {
                await RejectOpenAsync<NotSupportedException>(data, options);
            }
            await RejectOpenAsync<MediaMemoryLimitException>(data, budget: 16);
            byte[] unknownCluster = [0x1F, 0x43, 0xB6, 0x75, 0xFF,
                .. Enumerable.Range(0, 1000).SelectMany(_ => Field(0xEC, []))];
            await using EbmlElementTestSource source = new(Document(info, Tracks, unknownCluster));
            MediaBufferAllocator allocator = new(131072);
            await RejectAsync<NotSupportedException>(async () =>
            {
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator, new() { MaxElements = 30 });
            });
            Require(source.ReadCalls < 200 && allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyCancellationAndConcurrentReadAsync()
        {
            byte[] data = Document(Field(0x1549A966, InfoFields), Tracks,
                Cluster(Unsigned(0xE7, 0), Block(track: 5, payload: [1, 2, 3])));
            MediaBufferAllocator allocator = new(131072);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(data) { ReadGate = gate.Task, ReadGateOffset = data.Length - 3 };
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            long retained = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            ValueTask<MediaPacket?> pending = reader.ReadPacketAsync(cancellation.Token);
            Require(!pending.IsCompleted);
            await RejectAsync<InvalidOperationException>(async () => await reader.ReadPacketAtAsync(0, 0));
            await RejectAsync<InvalidOperationException>(async () => await reader.DisposeAsync());
            await cancellation.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await pending);
            Require(allocator.AllocatedBytes == retained && !source.IsDisposed);
            gate.SetResult();
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Require(packet.Data.Span.SequenceEqual(new byte[] { 1, 2, 3 }) && await reader.ReadPacketAsync() is null);
            packet.Dispose();
            await reader.DisposeAsync();
            Require(allocator.AllocatedBytes == 0);

            await using EbmlElementTestSource gated = new(data) { ReadGate = new TaskCompletionSource().Task };
            using CancellationTokenSource opening = new();
            ValueTask<MatroskaReader> open = MatroskaReader.OpenAsync(gated, allocator, cancellationToken: opening.Token);
            Require(!open.IsCompleted);
            await opening.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await open);
            Require(allocator.AllocatedBytes == 0 && !gated.IsDisposed);
        }
    }
}
