// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class MatroskaMetadataTestData
    {
        public static byte[] InfoFields => [.. Field(0x4D80, "a"u8), .. Field(0x5741, "bb"u8)];

        public static byte[] Header(uint id, long size)
        {
            byte[] data = new byte[12];
            BinaryPrimitives.WriteUInt32BigEndian(data, id);
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(4), (1UL << 56) | (ulong)size);
            int first = 0;
            while (data[first] == 0)
            {
                first++;
            }
            return data[first..];
        }

        public static byte[] Field(uint id, ReadOnlySpan<byte> value) => [.. Header(id, value.Length), .. value];

        public static byte[] Unsigned(uint id, ulong value)
        {
            byte[] bytes = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            return Field(id, bytes);
        }

        public static byte[] Float(uint id, double value)
        {
            byte[] bytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            return Field(id, bytes);
        }

        public static byte[] TrackFields(ulong number = 1, ulong type = 2) =>
            [.. Unsigned(0xD7, number), .. Unsigned(0x73C5, 16 + number), .. Unsigned(0x83, type), .. Field(0x86, "A_AAC"u8)];

        public static Task<MatroskaSegmentInfo> InfoAsync(byte[] payload, MatroskaMetadataLimits? limits = null)
            => ReadAsync(0x1549A966, payload, static (reader, element) => MatroskaInfoReader.ReadAsync(reader, element), limits);

        public static Task<IReadOnlyList<MatroskaTrack>> TracksAsync(byte[] payload, MatroskaMetadataLimits? limits = null)
            => ReadAsync(0x1654AE6B, payload, static (reader, element) => MatroskaTracksReader.ReadAsync(reader, element), limits);

        public static Task<MatroskaAudioSettings> AudioAsync(byte[] payload)
            => ReadAsync(0xE1, payload, static (reader, element) => MatroskaAudioReader.ReadAsync(reader, element, default));

        public static Task<MatroskaVideoSettings> VideoAsync(byte[] payload)
            => ReadAsync(0xE0, payload, static (reader, element) => MatroskaVideoReader.ReadAsync(reader, element, default));

        private static async Task<T> ReadAsync<T>(uint id, byte[] payload,
            Func<MatroskaMetadataReader, EbmlElement, ValueTask<T>> read, MatroskaMetadataLimits? limits = null)
        {
            await using EbmlElementTestSource source = new(payload) { MaximumRead = 3 };
            MediaBufferAllocator allocator = new(16384);
            try
            {
                using MatroskaMetadataReader reader = new(source, allocator, limits ?? new(8, 8192, 512));
                T value = await read(reader, new(id, 0, payload.Length));
                Require(allocator.AllocatedBytes == 4108);
                return value;
            }
            finally
            {
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
        }

        public static async Task RejectAsync<TException>(Func<Task> operation) where TException : Exception
        {
            try
            {
                await operation();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid Matroska metadata operation was accepted.");
        }

        public static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("A Matroska metadata value, bound or resource lifetime is incorrect.");
            }
        }
    }
}
