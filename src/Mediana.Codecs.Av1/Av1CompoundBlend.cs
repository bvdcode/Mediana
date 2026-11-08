// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1CompoundBlend(Span<short> predictionStorage, Av1CompoundPrediction compound = default,
        Span<byte> maskStorage = default, int maskStride = 0, int subsampling = 0)
    {
        private readonly Span<short> prediction = predictionStorage;
        private readonly Span<byte> mask = maskStorage;

        internal bool Active => !prediction.IsEmpty;
        internal bool Second => compound.Weights.Second != 0 || compound.Mask != Av1CompoundMaskType.None;

        internal byte Apply(int row, int column, int width, int value)
        {
            int index = row * width + column;
            if (!Second) { prediction[index] = checked((short)value); return 0; }
            int first = prediction[index];
            if (compound.Mask == Av1CompoundMaskType.None)
            {
                int sum = compound.Weights.First * first + compound.Weights.Second * value;
                return (byte)Math.Clamp((sum + 128) >> 8, 0, 255);
            }
            int position = (row << subsampling) * maskStride + (column << subsampling);
            int weight;
            if (subsampling == 0)
            {
                if (compound.Mask == Av1CompoundMaskType.Difference)
                {
                    int difference = (Math.Abs(first - value) + 8) >> 4;
                    int m = Math.Min(64, 38 + difference / 16);
                    mask[position] = (byte)(compound.Sign == 0 ? m : 64 - m);
                }
                weight = mask[position];
            }
            else
            {
                weight = (mask[position] + mask[position + 1] + mask[position + maskStride] + mask[position + maskStride + 1] + 2) >> 2;
            }
            return (byte)Math.Clamp((weight * first + (64 - weight) * value + 512) >> 10, 0, 255);
        }
    }
}
