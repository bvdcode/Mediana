// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using static Mediana.Tests.MatroskaAttachmentTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;
using static Mediana.Tests.MatroskaIndexTestData;

namespace Mediana.Tests
{
    internal static class MatroskaAttachmentTestInput
    {
        public static async Task VerifyOrderingAndOwnershipAsync()
        {
            byte[] data = Document(Field(0x1549A966, InfoFields), Tracks,
                Attachments(Entry([Field(0x467E, "Front"u8), .. Fields]),
                    Entry([Unsigned(0x46AE, 42), Field(0x465C, []), Field(0x4660, "font/ttf"u8), Field(0x466E, "../шрифт.ttf"u8)])),
                Cluster(Unsigned(0xE7, 0), Block(track: 5, payload: [4, 5, 6])));
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 1 };
            MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            IReadOnlyList<MatroskaAttachmentInfo> declarations = reader.Attachments;
            Require(declarations.Count == 2 && ReferenceEquals(declarations, ((IContainerAttachmentReader)reader).Attachments));
            Require(reader.Streams.Count == 1 && reader is not IContainerPictureReader);
            Require(declarations[0].Uid == ulong.MaxValue && declarations[0].FileName == "cover.png" &&
                declarations[0].MediaType == "image/png" && declarations[0].Description == "Front" && declarations[0].ByteLength == 3);
            Require(declarations[1].Uid == 42 && declarations[1].FileName == "../шрифт.ttf" && declarations[1].ByteLength == 0);
            await RejectAsync<ArgumentOutOfRangeException>(async () => await reader.ReadAttachmentAsync(-1));
            await RejectAsync<ArgumentOutOfRangeException>(async () => await reader.ReadAttachmentAsync(2));
            long live = allocator.AllocatedBytes;
            byte[] prefix = new byte[] { 99, 99, 99, 99, 99 };
            Require(await reader.ReadAttachmentPrefixAsync(0, prefix) == 3 &&
                prefix.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 99, 99 }) && allocator.AllocatedBytes == live);
            Require(await reader.ReadAttachmentPrefixAsync(1, prefix) == 0 && allocator.AllocatedBytes == live);
            using (MediaBuffer empty = await reader.ReadAttachmentAsync(1))
            {
                Require(empty.Memory.IsEmpty && allocator.AllocatedBytes == live + 1);
            }
            using MediaBuffer picture = await reader.ReadAttachmentAsync(0);
            Require(picture.Memory.Span.SequenceEqual(new byte[] { 1, 2, 3 }) && allocator.AllocatedBytes == live + 3);
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Require(packet.Data.Span.SequenceEqual(new byte[] { 4, 5, 6 }) && await reader.ReadPacketAsync() is null);
            await reader.DisposeAsync();
            data.AsSpan().Clear();
            Require(declarations[0].Uid == ulong.MaxValue && picture.Memory.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
            await RejectAsync<ObjectDisposedException>(async () => await reader.ReadAttachmentAsync(0));
            picture.Dispose();
            packet.Dispose();
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyInvalidDeclarationsAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[][] fields = Fields;
            for (int index = 0; index < fields.Length; index++)
            {
                await RejectOpenAsync<InvalidDataException>(Document(info, Tracks,
                    Attachments(Entry(fields.Where((_, position) => position != index).ToArray()))));
                await RejectOpenAsync<InvalidDataException>(Document(info, Tracks,
                    Attachments(Entry([.. fields, fields[index]]))));
            }
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks, Attachments(Entry(
                [.. fields.Where((_, index) => index != 2), Unsigned(0x46AE, 0)]))));
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks, Attachments()));
            byte[] valid = Attachments(Entry(fields));
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks, valid, valid));
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks, Attachments(Entry(
                [.. fields, Field(0x467E, [0xFF])]))));
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks, Attachments(Entry(
                [.. fields, Field(0x467E, "one"u8), Field(0x467E, "two"u8)]))));
            byte[] truncated = Document(info, Tracks, valid);
            await RejectOpenAsync<InvalidDataException>(truncated[..^1]);
        }

        public static async Task VerifyLimitsAndDeferredDataAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[] entries = Attachments(Entry(Fields), Entry(Fields));
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, entries), new() { MaxAttachments = 1 });
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, Attachments(Entry(Fields))), new() { MaxAttachments = 0 });
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, entries), new() { MaxTextBytes = 25 });
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, entries), new() { MaxElements = 24 });
            int declarations = InfoFields.Length + NativeTrack().Length + entries.Length - Header(0x1941A469, 0).Length - 6;
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, entries), new() { MaxMetadataBytes = declarations - 1 });
            MediaBufferAllocator allocator = new(131072);
            await using (EbmlElementTestSource exact = new(Document(info, Tracks, entries)))
            {
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(exact, allocator,
                    new() { MaxMetadataBytes = declarations, MaxAttachmentBytes = 2, MaxElements = 25 });
                int calls = exact.ReadCalls;
                long live = allocator.AllocatedBytes;
                await RejectAsync<NotSupportedException>(async () => await reader.ReadAttachmentAsync(0));
                Require(exact.ReadCalls == calls && allocator.AllocatedBytes == live);
            }
            const long largeLength = 3L * 1024 * 1024 * 1024;
            byte[] entry = [.. Fields[..3].SelectMany(field => field), .. Header(0x465C, largeLength)];
            byte[] prefix = [.. Field(0x1A45DFA3, Field(0x4282, "matroska"u8)),
                .. Header(0x18538067, info.Length + Tracks.Length + 22 + entry.Length + largeLength), .. info, .. Tracks,
                .. Header(0x1941A469, 10 + entry.Length + largeLength), .. Header(0x61A7, entry.Length + largeLength), .. entry];
            byte[] prefixWithLookahead = [.. prefix, .. new byte[12]];
            await using EbmlElementTestSource sparse = new(prefixWithLookahead)
            {
                Length = prefix.Length + largeLength,
                ExcludedReadRange = (prefix.Length + 12, prefix.Length + largeLength)
            };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(sparse, allocator, new() { MaxAttachmentBytes = 12 }))
            {
                Require(reader.Attachments.Count == 1 && reader.Attachments[0].ByteLength == largeLength);
                Require(sparse.BytesRead < 4096 && allocator.AllocatedBytes < 8192);
                int calls = sparse.ReadCalls;
                await RejectAsync<NotSupportedException>(async () => await reader.ReadAttachmentAsync(0));
                Require(sparse.ReadCalls == calls);
                long bytes = sparse.BytesRead;
                long live = allocator.AllocatedBytes;
                byte[] head = new byte[12];
                head.AsSpan().Fill(255);
                Require(await reader.ReadAttachmentPrefixAsync(0, head) == 12 && head.All(value => value == 0));
                Require(sparse.BytesRead == bytes + 12 && allocator.AllocatedBytes == live);
                calls = sparse.ReadCalls;
                await RejectAsync<NotSupportedException>(async () => await reader.ReadAttachmentPrefixAsync(0, new byte[13]));
                Require(sparse.ReadCalls == calls && allocator.AllocatedBytes == live);
            }
            Require(allocator.AllocatedBytes == 0 && !sparse.IsDisposed);
            byte[] payload = new byte[32000];
            byte[] document = Document(info, Tracks, Attachments(Entry([.. Fields[..3], Field(0x465C, payload)])));
            MediaBufferAllocator smallBudget = new(16384);
            await using EbmlElementTestSource bounded = new(document);
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(bounded, smallBudget))
            {
                long live = smallBudget.AllocatedBytes;
                int calls = bounded.ReadCalls;
                await RejectAsync<MediaMemoryLimitException>(async () => await reader.ReadAttachmentAsync(0));
                Require(smallBudget.AllocatedBytes == live && bounded.ReadCalls == calls);
            }
            Require(smallBudget.AllocatedBytes == 0);
        }

        public static async Task VerifyChecksumScopeAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[] entry = Entry(Fields);
            byte[][] valid =
            [
                Field(0x1941A469, [.. Field(0xBF, [0x83, 0x69, 0xC1, 0x11]), .. entry]),
                Attachments(Entry([Field(0xBF, [0xAF, 0x61, 0xCB, 0xB2]), .. Fields]))
            ];
            MediaBufferAllocator allocator = new(131072);
            foreach (byte[] attachment in valid)
            {
                await using EbmlElementTestSource source = new(Document(info, Tracks, attachment)) { MaximumRead = 2 };
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator, new() { VerifyMediaChecksums = true });
                using MediaBuffer data = await reader.ReadAttachmentAsync(0);
                Require(data.Memory.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
            }
            byte[][] damaged =
            [
                Field(0x1941A469, [.. Field(0xBF, [0, 0, 0, 0]), .. entry]),
                Attachments(Entry([Field(0xBF, [0, 0, 0, 0]), .. Fields]))
            ];
            foreach (byte[] attachment in damaged)
            {
                byte[] document = Document(info, Tracks, attachment);
                await using (EbmlElementTestSource source = new(document))
                {
                    await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
                    Require(reader.Attachments.Count == 1);
                }
                await RejectOpenAsync<InvalidDataException>(document, new() { VerifyMediaChecksums = true });
            }
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks,
                Attachments(Entry([.. Fields, Field(0xBF, [0, 0, 0, 0])]))));
            await RejectOpenAsync<InvalidDataException>(Document(info, Tracks,
                Attachments(Entry([Field(0xBF, [0]), .. Fields]))));
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyCancellationAndConcurrencyAsync()
        {
            byte[] payload = Enumerable.Range(0, 128).Select(index => (byte)index).ToArray();
            byte[] data = Document(Field(0x1549A966, InfoFields), Tracks,
                Cluster(Unsigned(0xE7, 0), Block(track: 5)), Attachments(Entry([.. Fields[..3], Field(0x465C, payload)])));
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(data)
            {
                MaximumRead = 3,
                ReadGate = gate.Task,
                ReadGateOffset = data.Length - payload.Length + 12
            };
            MediaBufferAllocator allocator = new(131072);
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            long live = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            ValueTask<MediaBuffer> pending = reader.ReadAttachmentAsync(0, cancellation.Token);
            Require(!pending.IsCompleted && allocator.AllocatedBytes == live + payload.Length);
            await RejectAsync<InvalidOperationException>(async () => await reader.ReadPacketAsync());
            await RejectAsync<InvalidOperationException>(async () => await reader.ReadPacketAtAsync(0, 0));
            await RejectAsync<InvalidOperationException>(async () => await reader.ReadAttachmentAsync(0));
            await RejectAsync<InvalidOperationException>(async () => await reader.DisposeAsync());
            await RejectAsync<InvalidOperationException>(() =>
            {
                reader.FindSeekPoint(0, new(0, TimeBase.TimeSpanTicks), 1);
                return Task.CompletedTask;
            });
            await cancellation.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await pending);
            Require(allocator.AllocatedBytes == live);
            gate.SetResult();
            using (MediaBuffer attachment = await reader.ReadAttachmentAsync(0))
            {
                Require(attachment.Memory.Span.SequenceEqual(payload));
            }
            source.ReadBoundary = data.Length - 1;
            await RejectAsync<InvalidOperationException>(async () => await reader.ReadAttachmentAsync(0));
            Require(allocator.AllocatedBytes == live);
            source.ReadBoundary = long.MaxValue;
            await reader.DisposeAsync();
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }
    }
}
