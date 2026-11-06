// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkNlsfReader
    {
        public static void Read(ref OpusRangeDecoder decoder, SilkNlsfCodebook codebook, SilkSignalType signal,
            scoped Span<sbyte> indices)
        {
            int model = signal switch
            {
                SilkSignalType.Inactive or SilkSignalType.Unvoiced => 0,
                SilkSignalType.Voiced => 32,
                _ => throw new ArgumentOutOfRangeException(nameof(signal))
            };
            ReadOnlySpan<byte> first = codebook.FirstModel.Slice(model, 32);
            ArgumentOutOfRangeException.ThrowIfLessThan(indices.Length, codebook.Order + 1, nameof(indices));
            Span<byte> offsets = stackalloc byte[16], predictors = stackalloc byte[16];
            int firstIndex = decoder.ReadInverseCdf(first);
            indices[0] = (sbyte)firstIndex;
            codebook.Unpack(firstIndex, offsets, predictors);
            ReadOnlySpan<byte> models = codebook.ResidualModels;
            for (int at = 0; at < codebook.Order; at++)
            {
                int residual = decoder.ReadInverseCdf(models.Slice(offsets[at], 9));
                if (residual == 0) { residual -= decoder.ReadInverseCdf([100, 40, 16, 7, 3, 1, 0]); }
                else if (residual == 8) { residual += decoder.ReadInverseCdf([100, 40, 16, 7, 3, 1, 0]); }
                indices[at + 1] = (sbyte)(residual - 4);
            }
        }

        public static int ReadInterpolation(ref OpusRangeDecoder decoder, int subframes) => subframes switch
        {
            2 => 4,
            4 => decoder.ReadInverseCdf([243, 221, 192, 181, 0]),
            _ => throw new ArgumentOutOfRangeException(nameof(subframes))
        };

        public static void Dequantize(SilkNlsfCodebook codebook, ReadOnlySpan<sbyte> indices, Span<short> frequenciesQ15)
        {
            ReadOnlySpan<short> weights = codebook.Weights;
            ArgumentOutOfRangeException.ThrowIfLessThan(indices.Length, codebook.Order + 1, nameof(indices));
            ArgumentOutOfRangeException.ThrowIfLessThan(frequenciesQ15.Length, codebook.Order, nameof(frequenciesQ15));
            ArgumentOutOfRangeException.ThrowIfNegative(indices[0], nameof(indices));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(indices[0], 31, nameof(indices));
            foreach (sbyte index in indices.Slice(1, codebook.Order))
            {
                if (index is < -10 or > 10) { throw new ArgumentOutOfRangeException(nameof(indices)); }
            }
            Span<byte> offsets = stackalloc byte[16], predictors = stackalloc byte[16];
            codebook.Unpack(indices[0], offsets, predictors);
            ReadOnlySpan<byte> centers = codebook.Centers;
            int previous = 0, first = indices[0] * codebook.Order;
            for (int at = codebook.Order - 1; at >= 0; at--)
            {
                int predicted = previous * predictors[at] >> 8, residual = indices[at + 1] << 10;
                if (residual > 0) { residual -= 102; }
                else if (residual < 0) { residual += 102; }
                previous = predicted + (residual * codebook.QuantizationStepQ16 >> 16);
                int frequency = ((short)previous << 14) / weights[first + at] + (centers[first + at] << 7);
                frequenciesQ15[at] = (short)Math.Clamp(frequency, 0, 32767);
            }
            SilkNlsfStabilizer.Apply(frequenciesQ15[..codebook.Order], codebook.MinimumDistances);
        }
    }
}
