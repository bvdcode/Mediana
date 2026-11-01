// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;

namespace Mediana.Tests
{
    internal static class MatroskaBlockGroupTestInput
    {
        public static async Task VerifyValuesAsync()
        {
            byte[] block = Block(0xA1, timestamp: -17, flags: 0);
            byte[] bytes = [.. Signed(0xFB, -23), .. Unsigned(0x9B, ulong.MaxValue), .. Signed(0x75A2, long.MinValue),
                .. Field(0xA4, [1, 2]), .. block, .. Field(0x75A1, [3]), .. Signed(0xFB, long.MaxValue), .. Unsigned(0xFA, 8)];
            MatroskaBlockGroup group = await ReadAsync(bytes);
            Require(group.HasReferences && group.Block.Id == 0xA1 && group.Block.DataSize == 5);
            Require(group.Extensions.DurationTicks == ulong.MaxValue && group.Extensions.DiscardPaddingNanoseconds == long.MinValue);
            Require(group.Extensions.CodecState is { Id: 0xA4, DataSize: 2 } && group.Extensions.BlockAdditions is { Id: 0x75A1, DataSize: 1 });
            Require(group.Extensions.ReferencePriority == 8);
            MatroskaBlockGroup empty = await ReadAsync(block);
            Require(!empty.HasReferences && !empty.Extensions.HasValues);
            MatroskaBlockGroup zero = await ReadAsync([.. block, .. Field(0xFB, [])]);
            Require(zero.HasReferences && !zero.Extensions.HasValues);
            MatroskaBlockGroup defaults = await ReadAsync([.. block, .. Field(0x75A2, []), .. Field(0x9B, []), .. Field(0xFA, [])]);
            Require(defaults.Extensions.DiscardPaddingNanoseconds == 0 && defaults.Extensions.DurationTicks == 0 && defaults.Extensions.ReferencePriority == 0);
        }

        public static async Task VerifyInvalidAsync()
        {
            byte[] block = Block(0xA1, flags: 0);
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([]));
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. block]));
            foreach (byte[] value in new[] { Unsigned(0x9B, 1), Signed(0x75A2, 1), Field(0xA4, []), Field(0x75A1, []), Unsigned(0xFA, 0) })
            {
                await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. value, .. value]));
            }
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. Signed(0xFB, 0), .. Signed(0xFB, -1)]));
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. Signed(0xFB, 1), .. Signed(0xFB, 0)]));
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. Field(0xFB, new byte[9])]));
            await RejectAsync<InvalidDataException>(async () => await ReadAsync([.. block, .. Field(0x75A2, new byte[9])]));
        }

        private static async Task<MatroskaBlockGroup> ReadAsync(byte[] bytes)
        {
            MediaBufferAllocator allocator = new(4108);
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 3 };
            try
            {
                using MatroskaMetadataReader reader = new(source, allocator, Limits);
                return await MatroskaBlockGroupReader.ReadAsync(reader, new(0xA0, 0, bytes.Length));
            }
            finally
            {
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
        }
    }
}
