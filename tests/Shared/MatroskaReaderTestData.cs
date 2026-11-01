// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaReaderTestData
    {
        public static byte[] Document(params byte[][] fields) =>
            [.. Field(0x1A45DFA3, Field(0x4282, "matroska"u8)),
                .. Field(0x18538067, fields.SelectMany(value => value).ToArray())];

        public static byte[] NativeTrack(ulong number = 5) => Field(0xAE,
            [.. Unsigned(0xD7, number), .. Unsigned(0x73C5, number + 1), .. Unsigned(0x83, 2),
                .. Field(0x86, "A_UNKNOWN"u8), .. Field(0xE1, []), .. Field(0x63A2, [10, 20]),
                .. Field(0x536E, "Sound"u8), .. Field(0x22B59D, "ru-RU"u8)]);

        public static byte[] Tracks => Field(0x1654AE6B, NativeTrack());

        public static async Task RejectOpenAsync<TException>(byte[] data, MatroskaReadOptions? options = null,
            long budget = 131072) where TException : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            await RejectAsync<TException>(async () =>
            {
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator, options);
            });
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }
    }
}
