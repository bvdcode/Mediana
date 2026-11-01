// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaIndexTestData
    {
        public const int ScratchBytes = 7448;
        public static MatroskaIndexOptions Options => new(65536, 1024, 100000, 4096, 1048576, true);
        public static MatroskaMetadataLimits Limits => new(8, 8192, 8192);

        public static MatroskaTrack Track(ulong number = 1) => new(number, number + 16, 2, "A_AAC",
            new(true, true, false, true), new(1, null, 0, 0), new(null, "eng", null), null, null, null, null, null);

        public static byte[] Signed(uint id, long value)
        {
            byte[] bytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            return Field(id, bytes);
        }

        public static byte[] Block(uint id = 0xA3, byte track = 1, short timestamp = 0, byte flags = 0x80,
            byte[]? payload = null)
        {
            byte[] prefix = [checked((byte)(0x80 | track)), 0, 0, flags];
            BinaryPrimitives.WriteInt16BigEndian(prefix.AsSpan(1), timestamp);
            return Field(id, [.. prefix, .. (payload ?? [0xAB])]);
        }

        public static byte[] Cluster(params byte[][] fields) => Field(0x1F43B675, fields.SelectMany(value => value).ToArray());

        public static ValueTask<MatroskaPacketIndex> BuildAsync(EbmlElementTestSource source, MediaBufferAllocator allocator,
            IReadOnlyList<MatroskaTrack>? tracks = null, MatroskaIndexOptions? options = null, CancellationToken token = default)
            => MatroskaPacketIndexBuilder.ReadAsync(source, allocator, new(0x18538067, 0, source.Length), tracks ?? [Track()],
                options ?? Options, Limits, token: token);

        public static async Task RejectBuildAsync<TException>(byte[] data, IReadOnlyList<MatroskaTrack>? tracks = null,
            MatroskaIndexOptions? options = null, long budget = 131072) where TException : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3 };
            await RejectAsync<TException>(async () =>
            {
                using MatroskaPacketIndex index = await BuildAsync(source, allocator, tracks, options);
            });
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }
    }
}
