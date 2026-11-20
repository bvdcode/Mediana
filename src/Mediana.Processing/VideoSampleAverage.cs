// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Processing
{
    internal static class VideoSampleAverage
    {
        private const int CancellationBlockSamples = 4096;

        public static void Apply<T>(ReadOnlySpan<T> first, ReadOnlySpan<T> second, Span<T> output, CancellationToken token)
            where T : unmanaged, IBinaryInteger<T>
        {
            int index = 0;
            while (index < output.Length)
            {
                token.ThrowIfCancellationRequested();
                int end = index + Math.Min(CancellationBlockSamples, output.Length - index);
                if (Vector.IsHardwareAccelerated)
                {
                    for (; index <= end - Vector<T>.Count; index += Vector<T>.Count)
                    {
                        Vector<T> a = new(first.Slice(index, Vector<T>.Count));
                        Vector<T> b = new(second.Slice(index, Vector<T>.Count));
                        ((a | b) - ((a ^ b) >> 1)).CopyTo(output.Slice(index, Vector<T>.Count));
                    }
                }
                for (; index < end; index++)
                {
                    T a = first[index], b = second[index];
                    output[index] = (a | b) - ((a ^ b) >> 1);
                }
            }
        }
    }
}
