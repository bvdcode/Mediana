// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacAnalysis
    {
        public static void Transform(ReadOnlySpan<double> input, int sequence, int shape, int previousShape,
            Span<double> spectrum, Span<double> windowed, Span<double> folded, Span<Complex> work,
            CancellationToken token)
        {
            if (input.Length != 2048 || spectrum.Length != 1024 || windowed.Length != 2048 ||
                folded.Length < 1024 || work.Length < 512)
            {
                throw new ArgumentException("AAC analysis requires a complete 2048-sample input and 1024-coefficient workspace.");
            }
            token.ThrowIfCancellationRequested();
            switch (sequence)
            {
                case 0:
                case 1:
                case 3:
                    input.CopyTo(windowed);
                    AacWindowing.ApplyLong(windowed, sequence, shape, previousShape);
                    AacMdct.Transform(windowed, spectrum, folded, windowed, work, token);
                    break;
                case 2:
                    ReadOnlySpan<double> left = AacWindows.Get(128, previousShape);
                    ReadOnlySpan<double> right = AacWindows.Get(128, shape);
                    Span<double> block = windowed[..256];
                    for (int window = 0; window < 8; window++)
                    {
                        token.ThrowIfCancellationRequested();
                        int start = 448 + window * 128;
                        for (int index = 0; index < 128; index++)
                        {
                            block[index] = input[start + index] * left[index];
                            block[128 + index] = input[start + 128 + index] * right[127 - index];
                        }
                        AacMdct.Transform(block, spectrum.Slice(window * 128, 128), folded, block, work, token);
                        left = right;
                    }
                    break;
                default:
                    throw new InvalidDataException("The AAC window sequence is invalid.");
            }
        }
    }
}
