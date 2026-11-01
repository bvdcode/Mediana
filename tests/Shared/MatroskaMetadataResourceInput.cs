// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Formats;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaMetadataResourceInput
    {
        private static byte[] CrcPayload => [.. InfoFields, .. Field(0xEC,
            Enumerable.Range(0, 9001).Select(static index => (byte)((index * 37 + 11) & 255)).ToArray())];

        public static async Task VerifyCrcAsync()
        {
            byte[] payload = CrcPayload;
            foreach (int chunk in new[] { 1, 3, 4096, 4097, payload.Length })
            {
                uint checksum = 0;
                for (int offset = 0; offset < payload.Length; offset += chunk)
                {
                    checksum = IeeeCrc32.Append(checksum, payload.AsSpan(offset, Math.Min(chunk, payload.Length - offset)));
                    checksum = IeeeCrc32.Append(checksum, []);
                }
                Require(checksum == 0x468261E0);
            }
            byte[] crc = Field(0xBF, [0xE0, 0x61, 0x82, 0x46]);
            Require((await InfoAsync([.. crc, .. payload])).TimestampScale == 1000000);
            payload[^1] ^= 1;
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. crc, .. payload]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. crc]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. Field(0xBF, [0x46, 0x82, 0x61, 0xE0]), .. CrcPayload]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. Field(0xBF, [0, 0, 0]), .. InfoFields]));
            Require((await InfoAsync([.. InfoFields, .. Field(0x80, [])])).Title is null);
        }

        public static async Task VerifyLimitsAsync()
        {
            Require((await InfoAsync(InfoFields, new(1, 3, 2))).WritingApp == "bb");
            await RejectAsync<NotSupportedException>(async () => await InfoAsync(InfoFields, new(1, 2, 2)));
            await RejectAsync<NotSupportedException>(async () => await InfoAsync(InfoFields, new(1, 3, 1)));
            byte[] text = [.. InfoFields, .. Field(0x7BA9, new byte[5000])];
            Require((await InfoAsync(text, new(1, 5003, 3))).Title == "");
            await RejectAsync<NotSupportedException>(async () => await InfoAsync(text, new(1, 5002, 3)));
            foreach (int quota in new[] { 4095, 4107 })
            {
                MediaBufferAllocator allocator = new(quota);
                await using EbmlElementTestSource source = new(InfoFields);
                await RejectAsync<MediaMemoryLimitException>(() => Task.FromResult(new MatroskaMetadataReader(source, allocator, new(1, 10, 10))));
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
            await using EbmlElementTestSource borrowed = new(InfoFields);
            MediaBufferAllocator budget = new(4108);
            await RejectAsync<ArgumentOutOfRangeException>(() => Task.FromResult(new MatroskaMetadataReader(borrowed, budget, new(1, 10, 10), 9)));
            Require(budget.AllocatedBytes == 0);
            using (MatroskaMetadataReader reader = new(borrowed, budget, new(1, 10, 10)))
            {
                reader.ReserveTrack();
                await RejectAsync<NotSupportedException>(() => { reader.ReserveTrack(); return Task.CompletedTask; });
                reader.Dispose();
                await RejectAsync<ObjectDisposedException>(async () => await reader.ReadUnsignedAsync(new(0xD7, 0, 1), 0, default));
            }
            Require(budget.AllocatedBytes == 0 && !borrowed.IsDisposed);
        }

        public static async Task VerifyCancellationAsync()
        {
            byte[] text = Field(0x7BA9, Enumerable.Repeat((byte)'x', 5000).ToArray());
            byte[][] inputs = [[.. text, .. InfoFields], [.. Field(0xBF, [0xE0, 0x61, 0x82, 0x46]), .. CrcPayload]];
            for (int index = 0; index < inputs.Length; index++)
            {
                TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                long gateOffset = index switch { 0 => 10, 1 => 4109, _ => throw new InvalidOperationException() };
                await using EbmlElementTestSource source = new(inputs[index]) { ReadGate = gate.Task, ReadGateOffset = gateOffset };
                MediaBufferAllocator allocator = new(9108);
                using (MatroskaMetadataReader reader = new(source, allocator, new(1, 5010, 10)))
                using (CancellationTokenSource cancellation = new())
                {
                    ValueTask<MatroskaSegmentInfo> pending = MatroskaInfoReader.ReadAsync(reader, new(0x1549A966, 0, inputs[index].Length), cancellation.Token);
                    Require(!pending.IsCompleted && allocator.AllocatedBytes == (index == 0 ? 9108 : 4108));
                    await cancellation.CancelAsync();
                    await RejectAsync<OperationCanceledException>(async () => await pending);
                    Require(allocator.AllocatedBytes == 4108);
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
                gate.SetResult();
                using MatroskaMetadataReader retry = new(source, allocator, new(1, 5010, 10));
                MatroskaSegmentInfo result = await MatroskaInfoReader.ReadAsync(retry, new(0x1549A966, 0, inputs[index].Length));
                Require(result.MuxingApp == "a" && (index != 0 || result.Title?.Length == 5000));
            }
            await using EbmlElementTestSource untouched = new(InfoFields);
            MediaBufferAllocator exact = new(4108);
            using (MatroskaMetadataReader reader = new(untouched, exact, new(1, 10, 10)))
            using (CancellationTokenSource canceled = new())
            {
                await canceled.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => await MatroskaInfoReader.ReadAsync(reader, new(0x1549A966, 0, InfoFields.Length), canceled.Token));
                Require(untouched.ReadCalls == 0);
            }
            Require(exact.AllocatedBytes == 0);
        }

        public static async Task VerifyDeferredAndTruncatedAsync()
        {
            const long privateBytes = 1L << 40;
            byte[] fields = TrackFields();
            byte[] privateHeader = Header(0x63A2, privateBytes);
            byte[] entryHeader = Header(0xAE, fields.Length + privateHeader.Length + privateBytes);
            byte[] prefix = [.. entryHeader, .. fields, .. privateHeader, .. new byte[12]];
            long end = entryHeader.Length + fields.Length + privateHeader.Length + privateBytes;
            await using (EbmlElementTestSource source = new(prefix) { Length = end, ReadBoundary = prefix.Length })
            {
                MediaBufferAllocator allocator = new(4108);
                using (MatroskaMetadataReader reader = new(source, allocator, new(1, 5, 6)))
                {
                    MatroskaTrack track = (await MatroskaTracksReader.ReadAsync(reader, new(0x1654AE6B, 0, end)))[0];
                    Require(track.CodecPrivate?.DataSize == privateBytes && track.CodecPrivate.Value.EndOffset == end);
                    Require(allocator.AllocatedBytes == 4108 && source.ReadCalls < 16);
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
            byte[] input = InfoFields;
            for (int size = 0; size < input.Length; size++)
            {
                await using EbmlElementTestSource truncated = new(input.AsMemory(0, size)) { Length = input.Length };
                MediaBufferAllocator allocator = new(4108);
                using (MatroskaMetadataReader reader = new(truncated, allocator, new(1, 10, 10)))
                {
                    await RejectAsync<EndOfStreamException>(async () => await MatroskaInfoReader.ReadAsync(reader, new(0x1549A966, 0, input.Length)));
                }
                Require(allocator.AllocatedBytes == 0 && !truncated.IsDisposed);
            }
        }
    }
}
