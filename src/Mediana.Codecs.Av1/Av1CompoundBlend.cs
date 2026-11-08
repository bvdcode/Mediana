// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1CompoundBlend<T>(Span<ushort> predictionStorage, Av1CompoundPrediction compound = default,
        Span<byte> maskStorage = default, int maskStride = 0, int subsampling = 0, int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
    {
        private readonly Span<ushort> prediction = predictionStorage;
        private readonly Span<byte> mask = maskStorage;
        private readonly Av1InterPrecision<T> precision = new(bitDepth);

        internal bool Active => !prediction.IsEmpty;
        internal bool Second => compound.Weights.Second != 0 || compound.Mask != Av1CompoundMaskType.None;

        internal T Apply(int row, int column, int width, int value)
        {
            int index = row * width + column;
            if (!Second) { prediction[index] = precision.StoreCompound(value); return T.Zero; }
            int first = precision.LoadCompound(prediction[index]);
            if (compound.Mask == Av1CompoundMaskType.None)
            {
                int sum = compound.Weights.First * first + compound.Weights.Second * value;
                int shift = precision.PostShift + 4;
                return T.CreateTruncating(Math.Clamp((sum + (1 << (shift - 1))) >> shift, 0, precision.Maximum));
            }
            int position = (row << subsampling) * maskStride + (column << subsampling);
            int weight;
            if (subsampling == 0)
            {
                if (compound.Mask == Av1CompoundMaskType.Difference)
                {
                    int shift = precision.DifferenceShift;
                    int difference = (Math.Abs(first - value) + (1 << (shift - 1))) >> shift;
                    int m = Math.Min(64, 38 + difference / 16);
                    mask[position] = (byte)(compound.Sign == 0 ? m : 64 - m);
                }
                weight = mask[position];
            }
            else
            {
                weight = Av1MaskSampling.Weight(mask, maskStride, subsampling, row, column);
            }
            int maskShift = precision.PostShift + 6;
            return T.CreateTruncating(Math.Clamp((weight * first + (64 - weight) * value + (1 << (maskShift - 1))) >> maskShift, 0, precision.Maximum));
        }
    }
}
