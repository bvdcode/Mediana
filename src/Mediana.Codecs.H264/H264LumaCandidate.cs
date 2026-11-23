// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264LumaCandidate(ReadOnlySpan<byte> firstSamples, int firstRowStride,
        ReadOnlySpan<byte> secondSamples = default, int secondRowStride = 0)
    {
        private const int Block = 16;
        private readonly ReadOnlySpan<byte> first = firstSamples;
        private readonly ReadOnlySpan<byte> second = secondSamples;
        private readonly int firstStride = firstRowStride;
        private readonly int secondStride = secondRowStride;

        /// <summary>Returns the full SAD unless a partial sum already exceeds the rejection limit.</summary>
        public int Difference(ReadOnlySpan<byte> comparison, int maximumError)
        {
            if (comparison.Length != Block * Block)
            {
                throw new ArgumentException("The luma comparison must be a complete 16x16 block.", nameof(comparison));
            }
            if (second.IsEmpty)
            {
                return H264SampleDifference.Sum16x16(comparison, first, firstStride, maximumError);
            }
            int error = 0;
            for (int row = 0; row < Block; row += 4)
            {
                Vector128<ushort> partial = Vector128<ushort>.Zero;
                for (int offset = 0; offset < 4; offset++)
                {
                    Vector128<byte> a = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(first), (nuint)((row + offset) * firstStride));
                    Vector128<byte> b = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(second), (nuint)((row + offset) * secondStride));
                    Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(comparison), (nuint)((row + offset) * Block));
                    partial += H264SampleDifference.Accumulate16(original, H264SampleAverage.Rounded(a, b));
                }
                error += H264SampleDifference.Total(partial);
                if (error > maximumError)
                {
                    break;
                }
            }
            return error;
        }

        public void Write(Span<byte> output)
        {
            if (output.Length != Block * Block)
            {
                throw new ArgumentException("The luma prediction must be a complete 16x16 block.", nameof(output));
            }
            if (second.IsEmpty)
            {
                for (int row = 0; row < Block; row++)
                {
                    first.Slice(row * firstStride, Block).CopyTo(output.Slice(row * Block, Block));
                }
            }
            else
            {
                for (int row = 0; row < Block; row++)
                {
                    Vector128<byte> a = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(first), (nuint)(row * firstStride));
                    Vector128<byte> b = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(second), (nuint)(row * secondStride));
                    H264SampleAverage.Rounded(a, b).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)(row * Block));
                }
            }
        }
    }
}
