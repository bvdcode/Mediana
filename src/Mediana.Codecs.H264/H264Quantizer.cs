// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264Quantizer
    {
        private static ReadOnlySpan<byte> ChromaValues =>
            [29, 30, 31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38, 39, 39, 39, 39];

        public H264Quantizer(int bitDepth)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bitDepth, 8);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bitDepth, 14);
            Offset = 6 * (bitDepth - 8);
        }

        public int Offset { get; }
        public int Maximum => 51 + Offset;
        public int MinimumDelta => -(26 + Offset / 2);
        public int MaximumDelta => 25 + Offset / 2;

        public int FromSyntax(int value)
        {
            if (value < -Offset || value > 51)
            {
                throw new InvalidDataException("The H.264 quantizer exceeds the sample precision range.");
            }
            return value + Offset;
        }

        public int ApplyDelta(int previous, int delta)
        {
            if (delta < MinimumDelta || delta > MaximumDelta)
            {
                throw new InvalidDataException("The H.264 quantizer delta exceeds the sample precision range.");
            }
            return (previous + delta + Maximum + 1) % (Maximum + 1);
        }

        public int ContextValue(int prime) => Math.Clamp(prime - Offset, 0, 51);

        public int ChromaValue(int lumaSyntax, int chromaOffset)
        {
            int index = Math.Clamp(lumaSyntax + chromaOffset, -Offset, 51);
            return (index < 30 ? index : ChromaValues[index - 30]) + Offset;
        }
    }
}
