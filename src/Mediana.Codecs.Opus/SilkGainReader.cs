// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkGainReader
    {
        public static void Read(ref OpusRangeDecoder decoder, scoped Span<byte> indices, int subframes,
            SilkSignalType signal, bool conditional)
        {
            if (subframes is not (2 or 4)) { throw new ArgumentOutOfRangeException(nameof(subframes)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(indices.Length, subframes, nameof(indices));
            ReadOnlySpan<byte> absoluteModel = signal switch
            {
                SilkSignalType.Inactive => AbsoluteModels[..8],
                SilkSignalType.Unvoiced => AbsoluteModels.Slice(8, 8),
                SilkSignalType.Voiced => AbsoluteModels.Slice(16, 8),
                _ => throw new ArgumentOutOfRangeException(nameof(signal))
            };
            if (conditional) { indices[0] = (byte)decoder.ReadInverseCdf(DeltaModel); }
            else
            {
                int high = decoder.ReadInverseCdf(absoluteModel);
                indices[0] = (byte)(8 * high + decoder.ReadInverseCdf([224, 192, 160, 128, 96, 64, 32, 0]));
            }
            for (int at = 1; at < subframes; at++) { indices[at] = (byte)decoder.ReadInverseCdf(DeltaModel); }
        }

        public static void Dequantize(ReadOnlySpan<byte> indices, Span<int> gainsQ16, ref int previousIndex, bool conditional)
        {
            if (indices.Length is not (2 or 4)) { throw new ArgumentException("SILK gains require two or four subframes.", nameof(indices)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(gainsQ16.Length, indices.Length, nameof(gainsQ16));
            ArgumentOutOfRangeException.ThrowIfNegative(previousIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(previousIndex, 63);
            int firstDelta = 0;
            if (!conditional)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(indices[0], 63, nameof(indices));
                firstDelta = 1;
            }
            foreach (byte index in indices[firstDelta..]) { ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 40, nameof(indices)); }
            int previous = previousIndex;
            for (int at = 0; at < indices.Length; at++)
            {
                if (at == 0 && !conditional) { previous = Math.Max(indices[at], previous - 16); }
                else { previous = Math.Max(2 * indices[at] - 16, previous + indices[at] - 4); }
                previous = Math.Clamp(previous, 0, 63);
                int logarithmQ7 = (0x1d1c71 * previous >> 16) + 2090;
                gainsQ16[at] = SilkFixedMath.Exp2Q7(logarithmQ7);
            }
            previousIndex = previous;
        }

        private static ReadOnlySpan<byte> AbsoluteModels =>
        [
            224, 112, 44, 15, 3, 2, 1, 0,
            254, 237, 192, 132, 70, 23, 4, 0,
            255, 252, 226, 155, 61, 11, 2, 0
        ];

        private static ReadOnlySpan<byte> DeltaModel =>
        [
            250, 245, 234, 203, 71, 50, 42, 38, 35, 33, 31, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20,
            19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0
        ];
    }
}
