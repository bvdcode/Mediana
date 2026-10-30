// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacMdct
    {
        public static void Transform(ReadOnlySpan<double> input, Span<double> spectrum, Span<double> folded,
            Span<double> time, Span<Complex> work, CancellationToken token)
        {
            int size = spectrum.Length;
            if (size is not (128 or 1024) || input.Length != 2 * size || folded.Length < size || time.Length < 2 * size)
            {
                throw new ArgumentException("AAC MDCT requires a complete supported analysis window and workspace.");
            }
            token.ThrowIfCancellationRequested();
            for (int index = 0; index < size / 2; index++)
            {
                folded[index] = -input[3 * size / 2 - 1 - index] - input[3 * size / 2 + index];
                folded[size / 2 + index] = input[index] - input[size - 1 - index];
            }
            AacImdct.Transform(folded[..size], time, work, token);
            for (int index = 0; index < size; index++)
            {
                spectrum[index] = -2 * size * time[3 * size / 2 - 1 - index];
            }
        }
    }
}
