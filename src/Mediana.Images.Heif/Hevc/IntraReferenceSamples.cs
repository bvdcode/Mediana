// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Substitutes unavailable intra neighbours in HEVC section 8.4.4.2.2 order.
    /// </summary>
    /// <remarks>
    /// Both spans contain 2 * size + 1 samples: index zero is the common corner,
    /// top[x + 1] is p[x,-1], and left[y + 1] is p[-1,y]. A value of -1 marks an
    /// unavailable sample. The caller owns both spans; substitution changes them in place.
    /// </remarks>
    internal static class IntraReferenceSamples
    {
        public static void Substitute(Span<int> top, Span<int> left, int size, int bitDepth)
        {
            Validate(top, left, size, bitDepth);
            int length = 2 * size;
            Span<int> scan = stackalloc int[129];
            for (int i = 0; i <= length; i++)
            {
                scan[i] = left[length - i];
            }
            top.Slice(1, length).CopyTo(scan.Slice(length + 1));

            int first = 0;
            while ((first <= (2 * length)) && (scan[first] == -1))
            {
                first++;
            }
            int previous = first > (2 * length) ? 1 << (bitDepth - 1) : scan[first];
            for (int i = 0; i <= (2 * length); i++)
            {
                if (scan[i] == -1)
                {
                    scan[i] = previous;
                }
                else
                {
                    previous = scan[i];
                }
            }

            for (int i = 0; i <= length; i++)
            {
                left[i] = scan[length - i];
            }
            top[0] = left[0];
            scan.Slice(length + 1, length).CopyTo(top.Slice(1));
        }

        internal static void Validate(ReadOnlySpan<int> top, ReadOnlySpan<int> left, int size, int bitDepth)
        {
            if ((size != 4) && (size != 8) && (size != 16) && (size != 32))
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if ((bitDepth != 8) && (bitDepth != 10))
            {
                throw new ArgumentOutOfRangeException(nameof(bitDepth));
            }
            if ((top.Length < ((2 * size) + 1)) || (left.Length < ((2 * size) + 1)))
            {
                throw new ArgumentException("Intra reference spans must include the corner and twice the block size.");
            }

            if (top[0] != left[0])
            {
                throw new ArgumentException("The intra reference corner must be shared.");
            }
            for (int i = 0; i <= (2 * size); i++)
            {
                if ((top[i] < -1) || (top[i] >= (1 << bitDepth)) || (left[i] < -1) || (left[i] >= (1 << bitDepth)))
                {
                    throw new ArgumentOutOfRangeException(nameof(top));
                }
            }
        }
    }
}
