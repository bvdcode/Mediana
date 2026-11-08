// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1SuperResolutionFilter
    {
        private const int ScaleBits = 14;
        private const int Scale = 1 << ScaleBits;
        private const int ScaleMask = Scale - 1;
        private const int ExtraBits = 8;
        private const int Taps = 8;
        private const int FilterOffset = 3;
        private const int FilterBits = 7;

        internal static void Row(ReadOnlySpan<byte> paddedSource, int codedWidth, Span<byte> output) =>
            RowCore(paddedSource, codedWidth, output, byte.MaxValue);

        internal static void Row(ReadOnlySpan<ushort> paddedSource, int codedWidth, Span<ushort> output, int bitDepth)
        {
            if (bitDepth is not (10 or 12)) { throw new ArgumentOutOfRangeException(nameof(bitDepth)); }
            RowCore(paddedSource, codedWidth, output, (1 << bitDepth) - 1);
        }

        private static void RowCore<T>(ReadOnlySpan<T> paddedSource, int codedWidth, Span<T> output, int maximum)
            where T : unmanaged, IBinaryInteger<T>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(codedWidth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(codedWidth, paddedSource.Length);
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, codedWidth);
            if (paddedSource.Overlaps(output)) { throw new ArgumentException("Super-resolution output must use distinct storage.", nameof(output)); }
            int upscaledWidth = output.Length;
            long step = ((long)codedWidth * Scale + upscaledWidth / 2) / upscaledWidth;
            long error = upscaledWidth * step - (long)codedWidth * Scale;
            long initial = (-((long)(upscaledWidth - codedWidth) * (Scale >> 1)) + upscaledWidth / 2) / upscaledWidth
                + (1 << (ExtraBits - 1)) - error / 2;
            long position = (initial & ScaleMask) - Scale;
            ReadOnlySpan<short> filters = Av1SuperResolutionFilters.Values;
            for (int column = 0; column < upscaledWidth; column++, position += step)
            {
                int origin = (int)(position >> ScaleBits);
                int phase = (int)((position & ScaleMask) >> ExtraBits);
                ReadOnlySpan<short> filter = filters.Slice(phase * Taps, Taps);
                int sum = 0;
                for (int tap = 0; tap < Taps; tap++)
                {
                    int source = (int)Math.Clamp((long)origin + tap - FilterOffset, 0, paddedSource.Length - 1);
                    sum += int.CreateChecked(paddedSource[source]) * filter[tap];
                }
                output[column] = T.CreateTruncating(Math.Clamp((sum + (1 << (FilterBits - 1))) >> FilterBits, 0, maximum));
            }
        }
    }
}
