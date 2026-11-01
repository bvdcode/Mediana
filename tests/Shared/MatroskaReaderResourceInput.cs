// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.IO;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    internal static class MatroskaReaderResourceInput
    {
        public static async Task VerifyChecksumsAndAggregateMetadataAsync()
        {
            byte[] info = Field(0x1549A966, InfoFields);
            byte[] crc = Field(0xBF, [0, 0, 0, 0]);
            byte[] cluster = Cluster(crc, Unsigned(0xE7, 0), Block(track: 5));
            byte[][] uncheckedMedia = [Document(info, Tracks, cluster), Document(crc, info, Tracks)];
            MediaBufferAllocator allocator = new(131072);
            foreach (byte[] bytes in uncheckedMedia)
            {
                await using EbmlElementTestSource source = new(bytes) { MaximumRead = 3 };
                await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
                {
                    Require(reader.Tracks.Count == 1);
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
                await RejectOpenAsync<InvalidDataException>(bytes, new() { VerifyMediaChecksums = true });
            }
            await RejectOpenAsync<InvalidDataException>(Document(Field(0x1549A966, [.. crc, .. InfoFields]), Tracks));
            await RejectOpenAsync<InvalidDataException>(Document(info, crc, Tracks));
            int bytesTotal = InfoFields.Length + NativeTrack().Length;
            await using EbmlElementTestSource exact = new(Document(info, Tracks));
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(exact, allocator, new() { MaxMetadataBytes = bytesTotal }))
            {
                Require(reader.Tracks.Count == 1);
            }
            Require(allocator.AllocatedBytes == 0);
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks), new() { MaxMetadataBytes = bytesTotal - 1 });
            await RejectOpenAsync<NotSupportedException>(Document(info, Tracks, info), new() { MaxMetadataBytes = bytesTotal });
        }

        public static async Task VerifyFactoryAndSourceRequirementsAsync()
        {
            MatroskaReaderFactory factory = new();
            Require(factory.ImplementationId == "matroska.managed" && factory.ProbeLength == 4);
            byte[] signature = [0x1A, 0x45, 0xDF, 0xA3];
            for (int count = 0; count < signature.Length; count++)
            {
                Require(!factory.CanRead(signature.AsSpan(0, count)));
            }
            Require(factory.CanRead(signature) && !factory.CanRead("RIFF"u8));
            await RejectOpenAsync<NotSupportedException>([.. Field(0x1A45DFA3, Field(0x4282, "webm"u8)),
                .. Field(0x18538067, [])]);
            byte[] bytes = Document(Field(0x1549A966, InfoFields), Tracks);
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource unknownLength = new(bytes) { Length = null };
            await RejectAsync<NotSupportedException>(async () =>
            {
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(unknownLength, allocator);
            });
            Require(allocator.AllocatedBytes == 0 && !unknownLength.IsDisposed);
            await using StreamByteSource sequential = new(new MemoryStream(bytes));
            await RejectAsync<NotSupportedException>(async () => await factory.OpenAsync(sequential, allocator));
            Require(allocator.AllocatedBytes == 0);
        }
    }
}
