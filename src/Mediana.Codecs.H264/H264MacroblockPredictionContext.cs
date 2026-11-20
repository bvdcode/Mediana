// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264MacroblockPredictionContext(H264PictureStructure Structure, int X, int Y, bool AdaptiveField)
    {
        public static H264MacroblockPredictionContext Create(H264Picture picture, int x, int y)
        {
            if (!picture.Layout.MacroblockAdaptiveFrameField)
            {
                return new(picture.Structure, x, y, false);
            }
            return CreateAdaptive(picture, x, y);
        }

        private static H264MacroblockPredictionContext CreateAdaptive(H264Picture picture, int x, int y)
        {
            int address = picture.Grid.Address(x, y);
            if (!picture.Grid.IsField(address))
            {
                return new(H264PictureStructure.Frame, x, y, false);
            }
            H264PictureStructure structure = (address & 1) switch
            {
                0 => H264PictureStructure.TopField,
                1 => H264PictureStructure.BottomField,
                _ => throw new InvalidOperationException("Invalid H.264 macroblock parity.")
            };
            return new(structure, x, y / 32 * 16 + y % 16, true);
        }

        public int ActiveReferenceCount(int count) => AdaptiveField ? checked(count * 2) : count;

        public int WeightIndex(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return AdaptiveField ? index / 2 : index;
        }
    }
}
