// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264DirectionalIntraPrediction
    {
        private const byte CornerIndex = 128;

        internal static void Write(int mode, int size, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output)
        {
            Vector128<byte> references = mode switch
            {
                3 or 7 => size == 4 ? Vector128.CreateScalar(MemoryMarshal.Read<ulong>(top)).AsByte()
                    : MemoryMarshal.Read<Vector128<byte>>(top),
                8 => size == 4 ? Vector128.CreateScalar(MemoryMarshal.Read<uint>(left)).AsByte()
                    : Vector128.CreateScalar(MemoryMarshal.Read<ulong>(left)).AsByte(),
                4 or 5 or 6 => Vector128.Create(MemoryMarshal.Read<ulong>(top), size == 4
                    ? MemoryMarshal.Read<uint>(left) : MemoryMarshal.Read<ulong>(left)).AsByte(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            Vector128<byte> cornerValue = Vector128.Create(corner.GetValueOrDefault());
            ReadOnlySpan<byte> table = size == 4 ? Intra4x4Indices : Intra8x8Indices;
            int count = size * size;
            ReadOnlySpan<byte> mapping = table.Slice((mode - 3) * 3 * count, 3 * count);
            bool crosses = mode is 4 or 5 or 6;
            for (int element = 0; element < count; element += 16)
            {
                int offset = element * 3;
                Vector128<byte> indicesA = MemoryMarshal.Read<Vector128<byte>>(mapping[offset..]);
                Vector128<byte> indicesB = MemoryMarshal.Read<Vector128<byte>>(mapping[(offset + 16)..]);
                Vector128<byte> indicesC = MemoryMarshal.Read<Vector128<byte>>(mapping[(offset + 32)..]);
                Vector128<byte> a = Select(references, indicesA, cornerValue, crosses);
                Vector128<byte> b = Select(references, indicesB, cornerValue, crosses);
                Vector128<byte> c = Select(references, indicesC, cornerValue, crosses);
                Vector128<byte> result = H264SampleAverage.Quarter(a, b, c);
                MemoryMarshal.Write(output[element..], in result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Select(Vector128<byte> references, Vector128<byte> indices,
            Vector128<byte> corner, bool crosses)
        {
            Vector128<byte> values = Vector128.ShuffleNative(references, indices);
            return crosses ? values | (Vector128.Equals(indices, Vector128.Create(CornerIndex)) & corner) : values;
        }

        // Three index vectors per 16 output samples, in H.264 mode order.
        // The corner index selects the top-left reference outside the packed rows.
        private static ReadOnlySpan<byte> Intra4x4Indices =>
        [
            0, 1, 2, 3, 1, 2, 3, 4, 2, 3, 4, 5, 3, 4, 5, 6,
            1, 2, 3, 4, 2, 3, 4, 5, 3, 4, 5, 6, 4, 5, 6, 7,
            2, 3, 4, 5, 3, 4, 5, 6, 4, 5, 6, 7, 5, 6, 7, 7,
            0, 128, 0, 1, 128, 0, 128, 0, 8, 128, 0, 128, 9, 8, 128, 0,
            128, 0, 1, 2, 8, 128, 0, 1, 9, 8, 128, 0, 10, 9, 8, 128,
            8, 1, 2, 3, 9, 8, 1, 2, 10, 9, 8, 1, 11, 10, 9, 8,
            128, 0, 1, 2, 8, 128, 0, 1, 9, 128, 0, 1, 10, 8, 128, 0,
            0, 1, 2, 3, 128, 0, 1, 2, 8, 0, 1, 2, 9, 128, 0, 1,
            128, 0, 1, 2, 0, 1, 2, 3, 128, 128, 0, 1, 8, 0, 1, 2,
            128, 0, 1, 2, 8, 128, 128, 0, 9, 8, 8, 128, 10, 9, 9, 8,
            8, 128, 0, 1, 9, 8, 8, 128, 10, 9, 9, 8, 11, 10, 10, 9,
            128, 8, 128, 0, 8, 9, 128, 8, 9, 10, 8, 9, 10, 11, 9, 10,
            0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 4, 1, 2, 3, 4,
            1, 2, 3, 4, 1, 2, 3, 4, 2, 3, 4, 5, 2, 3, 4, 5,
            0, 1, 2, 3, 2, 3, 4, 5, 1, 2, 3, 4, 3, 4, 5, 6,
            0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3,
            1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            0, 2, 1, 3, 1, 3, 2, 3, 2, 3, 3, 3, 3, 3, 3, 3,
        ];

        private static ReadOnlySpan<byte> Intra8x8Indices =>
        [
            0, 1, 2, 3, 4, 5, 6, 7, 1, 2, 3, 4, 5, 6, 7, 8,
            1, 2, 3, 4, 5, 6, 7, 8, 2, 3, 4, 5, 6, 7, 8, 9,
            2, 3, 4, 5, 6, 7, 8, 9, 3, 4, 5, 6, 7, 8, 9, 10,
            2, 3, 4, 5, 6, 7, 8, 9, 3, 4, 5, 6, 7, 8, 9, 10,
            3, 4, 5, 6, 7, 8, 9, 10, 4, 5, 6, 7, 8, 9, 10, 11,
            4, 5, 6, 7, 8, 9, 10, 11, 5, 6, 7, 8, 9, 10, 11, 12,
            4, 5, 6, 7, 8, 9, 10, 11, 5, 6, 7, 8, 9, 10, 11, 12,
            5, 6, 7, 8, 9, 10, 11, 12, 6, 7, 8, 9, 10, 11, 12, 13,
            6, 7, 8, 9, 10, 11, 12, 13, 7, 8, 9, 10, 11, 12, 13, 14,
            6, 7, 8, 9, 10, 11, 12, 13, 7, 8, 9, 10, 11, 12, 13, 14,
            7, 8, 9, 10, 11, 12, 13, 14, 8, 9, 10, 11, 12, 13, 14, 15,
            8, 9, 10, 11, 12, 13, 14, 15, 9, 10, 11, 12, 13, 14, 15, 15,
            0, 128, 0, 1, 2, 3, 4, 5, 128, 0, 128, 0, 1, 2, 3, 4,
            128, 0, 1, 2, 3, 4, 5, 6, 8, 128, 0, 1, 2, 3, 4, 5,
            8, 1, 2, 3, 4, 5, 6, 7, 9, 8, 1, 2, 3, 4, 5, 6,
            8, 128, 0, 128, 0, 1, 2, 3, 9, 8, 128, 0, 128, 0, 1, 2,
            9, 8, 128, 0, 1, 2, 3, 4, 10, 9, 8, 128, 0, 1, 2, 3,
            10, 9, 8, 1, 2, 3, 4, 5, 11, 10, 9, 8, 1, 2, 3, 4,
            10, 9, 8, 128, 0, 128, 0, 1, 11, 10, 9, 8, 128, 0, 128, 0,
            11, 10, 9, 8, 128, 0, 1, 2, 12, 11, 10, 9, 8, 128, 0, 1,
            12, 11, 10, 9, 8, 1, 2, 3, 13, 12, 11, 10, 9, 8, 1, 2,
            12, 11, 10, 9, 8, 128, 0, 128, 13, 12, 11, 10, 9, 8, 128, 0,
            13, 12, 11, 10, 9, 8, 128, 0, 14, 13, 12, 11, 10, 9, 8, 128,
            14, 13, 12, 11, 10, 9, 8, 1, 15, 14, 13, 12, 11, 10, 9, 8,
            128, 0, 1, 2, 3, 4, 5, 6, 8, 128, 0, 1, 2, 3, 4, 5,
            0, 1, 2, 3, 4, 5, 6, 7, 128, 0, 1, 2, 3, 4, 5, 6,
            128, 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 7,
            9, 128, 0, 1, 2, 3, 4, 5, 10, 8, 128, 0, 1, 2, 3, 4,
            8, 0, 1, 2, 3, 4, 5, 6, 9, 128, 0, 1, 2, 3, 4, 5,
            128, 128, 0, 1, 2, 3, 4, 5, 8, 0, 1, 2, 3, 4, 5, 6,
            11, 9, 128, 0, 1, 2, 3, 4, 12, 10, 8, 128, 0, 1, 2, 3,
            10, 8, 0, 1, 2, 3, 4, 5, 11, 9, 128, 0, 1, 2, 3, 4,
            9, 128, 128, 0, 1, 2, 3, 4, 10, 8, 0, 1, 2, 3, 4, 5,
            13, 11, 9, 128, 0, 1, 2, 3, 14, 12, 10, 8, 128, 0, 1, 2,
            12, 10, 8, 0, 1, 2, 3, 4, 13, 11, 9, 128, 0, 1, 2, 3,
            11, 9, 128, 128, 0, 1, 2, 3, 12, 10, 8, 0, 1, 2, 3, 4,
            128, 0, 1, 2, 3, 4, 5, 6, 8, 128, 128, 0, 1, 2, 3, 4,
            8, 128, 0, 1, 2, 3, 4, 5, 9, 8, 8, 128, 0, 1, 2, 3,
            128, 8, 128, 0, 1, 2, 3, 4, 8, 9, 128, 8, 128, 0, 1, 2,
            9, 8, 8, 128, 128, 0, 1, 2, 10, 9, 9, 8, 8, 128, 128, 0,
            10, 9, 9, 8, 8, 128, 0, 1, 11, 10, 10, 9, 9, 8, 8, 128,
            9, 10, 8, 9, 128, 8, 128, 0, 10, 11, 9, 10, 8, 9, 128, 8,
            11, 10, 10, 9, 9, 8, 8, 128, 12, 11, 11, 10, 10, 9, 9, 8,
            12, 11, 11, 10, 10, 9, 9, 8, 13, 12, 12, 11, 11, 10, 10, 9,
            11, 12, 10, 11, 9, 10, 8, 9, 12, 13, 11, 12, 10, 11, 9, 10,
            13, 12, 12, 11, 11, 10, 10, 9, 14, 13, 13, 12, 12, 11, 11, 10,
            14, 13, 13, 12, 12, 11, 11, 10, 15, 14, 14, 13, 13, 12, 12, 11,
            13, 14, 12, 13, 11, 12, 10, 11, 14, 15, 13, 14, 12, 13, 11, 12,
            0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7,
            1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8,
            0, 1, 2, 3, 4, 5, 6, 7, 2, 3, 4, 5, 6, 7, 8, 9,
            1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8,
            2, 3, 4, 5, 6, 7, 8, 9, 2, 3, 4, 5, 6, 7, 8, 9,
            1, 2, 3, 4, 5, 6, 7, 8, 3, 4, 5, 6, 7, 8, 9, 10,
            2, 3, 4, 5, 6, 7, 8, 9, 2, 3, 4, 5, 6, 7, 8, 9,
            3, 4, 5, 6, 7, 8, 9, 10, 3, 4, 5, 6, 7, 8, 9, 10,
            2, 3, 4, 5, 6, 7, 8, 9, 4, 5, 6, 7, 8, 9, 10, 11,
            3, 4, 5, 6, 7, 8, 9, 10, 3, 4, 5, 6, 7, 8, 9, 10,
            4, 5, 6, 7, 8, 9, 10, 11, 4, 5, 6, 7, 8, 9, 10, 11,
            3, 4, 5, 6, 7, 8, 9, 10, 5, 6, 7, 8, 9, 10, 11, 12,
            0, 0, 1, 1, 2, 2, 3, 3, 1, 1, 2, 2, 3, 3, 4, 4,
            1, 1, 2, 2, 3, 3, 4, 4, 2, 2, 3, 3, 4, 4, 5, 5,
            0, 2, 1, 3, 2, 4, 3, 5, 1, 3, 2, 4, 3, 5, 4, 6,
            2, 2, 3, 3, 4, 4, 5, 5, 3, 3, 4, 4, 5, 5, 6, 6,
            3, 3, 4, 4, 5, 5, 6, 6, 4, 4, 5, 5, 6, 6, 7, 7,
            2, 4, 3, 5, 4, 6, 5, 7, 3, 5, 4, 6, 5, 7, 6, 7,
            4, 4, 5, 5, 6, 6, 7, 7, 5, 5, 6, 6, 7, 7, 7, 7,
            5, 5, 6, 6, 7, 7, 7, 7, 6, 6, 7, 7, 7, 7, 7, 7,
            4, 6, 5, 7, 6, 7, 7, 7, 5, 7, 6, 7, 7, 7, 7, 7,
            6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        ];
    }
}
