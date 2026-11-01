// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Formats;

namespace Mediana.Tests
{
    internal static class MatroskaHeaderTestInput
    {
        private static ReadOnlySpan<byte> DocumentType => [0x42, 0x82, 0x88, 109, 97, 116, 114, 111, 115, 107, 97];

        public static async Task VerifyDefaultsAndVersionsAsync()
        {
            MatroskaHeader defaults = await ReadAsync(Wrap(DocumentType));
            Require(defaults == new MatroskaHeader(16, 1, 1, 1, 8));
            ushort[] identifiers = [0x4286, 0x42F7, 0x42F2, 0x42F3, 0x4287, 0x4285];
            byte[] empty = [.. DocumentType];
            foreach (ushort id in identifiers)
            {
                empty = [.. empty, .. Field(id, [])];
            }
            Require((await ReadAsync(Wrap(empty))) == defaults with { BodyOffset = empty.Length + 5 });
            for (byte width = 1; width <= 8; width++)
            {
                byte[] payload = [.. Field(0x42F3, [width]), .. DocumentType];
                byte[] wideHeader = [0x1A, 0x45, 0xDF, 0xA3, 1, 0, 0, 0, 0, 0, 0, (byte)payload.Length, .. payload];
                MatroskaHeader parsed = await ReadAsync(wideHeader);
                Require(parsed.MaximumSizeWidth == width && parsed.BodyOffset == wideHeader.Length);
            }
            byte[] future = [.. Field(0x4286, [99]), .. Field(0x4287, [99]), .. Field(0x4285, [2]), .. DocumentType];
            MatroskaHeader futureHeader = await ReadAsync(Wrap(future));
            Require(futureHeader.EbmlVersion == 99 && futureHeader.DocumentVersion == 99 && futureHeader.DocumentReadVersion == 2);
            byte[] unknown = [0xEC, 0x82, 0, 255, 0x80, 0x82, 1, 2, .. DocumentType, 0x80, 0x80, 0xEC, 0x80];
            Require((await ReadAsync(Wrap(unknown))).DocumentVersion == 1);
            byte[] padded = [0x42, 0x82, 0x8A, 109, 97, 116, 114, 111, 115, 107, 97, 0, 255];
            Require((await ReadAsync(Wrap(padded))).EbmlVersion == 1);
        }

        public static async Task VerifyInvalidFieldsAsync()
        {
            foreach (ushort id in new ushort[] { 0x4286, 0x42F7, 0x42F2, 0x42F3, 0x4287, 0x4285 })
            {
                byte[] value = Field(id, []);
                await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. value, .. value]));
                await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(id, [0])]));
                await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(id, new byte[9])]));
            }
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. DocumentType]));
            await RejectAsync<InvalidDataException>(Wrap([0xEC, 0x80]));
            await RejectAsync<InvalidDataException>(Wrap(Field(0x4282, [])));
            await RejectAsync<InvalidDataException>(Wrap(Field(0x4282, [0])));
            await RejectAsync<InvalidDataException>(Wrap(Field(0x4282, [128])));
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(0x42F2, [3])]));
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(0x42F2, [5])]));
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(0x42F3, [9])]));
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(0x42F7, [2])]));
            await RejectAsync<InvalidDataException>(Wrap([.. DocumentType, .. Field(0x4285, [2])]));
            await RejectAsync<NotSupportedException>(Wrap([.. DocumentType, .. Field(0x4286, [2]), .. Field(0x42F7, [2])]));
            await RejectAsync<NotSupportedException>(Wrap([.. DocumentType, .. Field(0x4287, [5]), .. Field(0x4285, [5])]));
            await RejectAsync<NotSupportedException>(Wrap([.. DocumentType, .. Field(0x4281, [])]));
            await RejectAsync<NotSupportedException>(Wrap(Field(0x4282, "webm"u8)));
        }

        public static async Task VerifyCrcAsync()
        {
            Require(IeeeCrc32.Compute([]) == 0 && IeeeCrc32.Compute("123456789"u8) == 0xCBF43926);
            byte[] crc = [0xBF, 0x84, 0x14, 0xB6, 0x1E, 0x26];
            byte[] valid = Wrap([.. crc, .. DocumentType]);
            Require((await ReadAsync(valid)).BodyOffset == valid.Length);
            await RejectAsync<InvalidDataException>(Wrap([0xBF, 0x84, 0x26, 0x1E, 0xB6, 0x14, .. DocumentType]));
            await RejectAsync<InvalidDataException>(Wrap([0xBF, 0x84, 0x15, 0xB6, 0x1E, 0x26, .. DocumentType]));
            await RejectAsync<InvalidDataException>(Wrap([0xBF, 0x83, 0, 0, 0, .. DocumentType]));
            await RejectAsync<InvalidDataException>(Wrap([0xEC, 0x80, .. crc, .. DocumentType]));
            await RejectAsync<InvalidDataException>(Wrap([.. crc, .. crc, .. DocumentType]));
            using CancellationTokenSource canceled = new();
            await canceled.CancelAsync();
            bool rejected = false;
            try
            {
                IeeeCrc32.Compute([], canceled.Token);
            }
            catch (OperationCanceledException)
            {
                rejected = true;
            }
            Require(rejected);
        }

        public static async Task VerifyBoundsAndTruncationAsync()
        {
            byte[] valid = Wrap(DocumentType);
            await RejectAsync<NotSupportedException>(valid, valid.Length - 1);
            Require((await ReadAsync(valid, valid.Length)).BodyOffset == valid.Length);
            await RejectAsync<ArgumentOutOfRangeException>(valid, 0);
            await RejectAsync<ArgumentOutOfRangeException>(valid, -1);
            await RejectAsync<InvalidDataException>([0xA0, 0x80]);
            await RejectAsync<InvalidDataException>([0x1A, 0x45, 0xDF, 0xA3, 0x80]);
            await RejectAsync<InvalidDataException>([0x1A, 0x45, 0xDF, 0xA3, 0xFF, .. DocumentType]);
            await RejectAsync<InvalidDataException>(Wrap([0xEC, 0xFF]));
            await RejectAsync<InvalidDataException>(Wrap([0xEC, 0x85, 0]));
            await RejectAsync<NotSupportedException>([0x1A, 0x45, 0xDF, 0xA3, 1, 255, 255, 255, 255, 255, 255, 254], knownLength: false);
            await RejectAsync<InvalidDataException>([]);
            for (int length = 1; length < valid.Length; length++)
            {
                await RejectAsync<EndOfStreamException>(valid[..length], knownLength: false);
            }
        }

        public static async Task VerifyCancellationAndQuotaAsync()
        {
            byte[] payload = [.. DocumentType, 0xEC, 0x8F, .. new byte[15]];
            byte[] input = Wrap(payload);
            MediaBufferAllocator exact = new(payload.Length);
            await using EbmlElementTestSource source = new(input) { MaximumRead = 1 };
            Require((await MatroskaHeaderReader.ReadAsync(source, exact, input.Length)).BodyOffset == input.Length);
            Require(exact.AllocatedBytes == 0 && !source.IsDisposed);
            MediaBufferAllocator insufficient = new(payload.Length - 1);
            await RejectOperationAsync<MediaMemoryLimitException>(async () => await MatroskaHeaderReader.ReadAsync(source, insufficient, input.Length));
            Require(insufficient.AllocatedBytes == 0 && !source.IsDisposed);
            foreach ((long gateOffset, int callsBeforeCancel, int liveBytes) in new[] { (0L, 1, 12), (5L, 2, payload.Length) })
            {
                TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await using EbmlElementTestSource pendingSource = new(input) { ReadGate = gate.Task, ReadGateOffset = gateOffset };
                using CancellationTokenSource pending = new();
                ValueTask<MatroskaHeader> read = MatroskaHeaderReader.ReadAsync(pendingSource, exact, input.Length, pending.Token);
                Require(!read.IsCompleted && pendingSource.ReadCalls == callsBeforeCancel && exact.AllocatedBytes == liveBytes);
                await pending.CancelAsync();
                await RejectOperationAsync<OperationCanceledException>(async () => await read);
                Require(exact.AllocatedBytes == 0 && !pendingSource.IsDisposed);
                gate.SetResult();
                Require((await MatroskaHeaderReader.ReadAsync(pendingSource, exact, input.Length)).BodyOffset == input.Length);
            }
            using CancellationTokenSource canceled = new();
            await canceled.CancelAsync();
            int calls = source.ReadCalls;
            await RejectOperationAsync<OperationCanceledException>(async () => await MatroskaHeaderReader.ReadAsync(source, exact, input.Length, canceled.Token));
            Require(source.ReadCalls == calls && exact.AllocatedBytes == 0);
        }

        public static async Task VerifyMuxedFileAsync()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "avc-aac-attachment.mkv"));
            await using EbmlElementTestSource source = new(input) { MaximumRead = 3, ReadBoundary = 40 };
            MediaBufferAllocator allocator = new(35);
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, 40);
            Require(header == new MatroskaHeader(40, 1, 4, 2, 8));
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static byte[] Field(ushort id, ReadOnlySpan<byte> value)
            => [(byte)(id >> 8), (byte)id, (byte)(0x80 | value.Length), .. value];

        private static byte[] Wrap(ReadOnlySpan<byte> payload)
            => [0x1A, 0x45, 0xDF, 0xA3, (byte)(0x80 | payload.Length), .. payload];

        private static async Task<MatroskaHeader> ReadAsync(byte[] input, int maximumHeaderBytes = 4096)
        {
            await using EbmlElementTestSource source = new(input) { MaximumRead = 1 };
            MediaBufferAllocator allocator = new(256);
            MatroskaHeader result = await MatroskaHeaderReader.ReadAsync(source, allocator, maximumHeaderBytes);
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            return result;
        }

        private static async Task RejectAsync<TException>(byte[] input, int maximumHeaderBytes = 4096, bool knownLength = true)
            where TException : Exception
        {
            await using EbmlElementTestSource source = new(input) { MaximumRead = 1, Length = knownLength ? input.Length : null };
            MediaBufferAllocator allocator = new(256);
            await RejectOperationAsync<TException>(async () => await MatroskaHeaderReader.ReadAsync(source, allocator, maximumHeaderBytes));
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static async Task RejectOperationAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid Matroska header operation was accepted.");
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("The Matroska document header, bounds or storage ownership changed.");
            }
        }
    }
}
