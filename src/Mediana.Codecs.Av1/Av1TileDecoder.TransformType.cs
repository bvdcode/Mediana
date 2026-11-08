// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int ReadTransform(int plane, int mode, bool inter, int x4, int y4, int lumaRow, int lumaColumn, Av1TransformSize size)
        {
            if (lossless || size.Maximum > 3) { return 0; }
            if (plane != 0)
            {
                int transform;
                if (inter)
                {
                    int row = Math.Max(lumaRow, y4 << 1);
                    int column = Math.Max(lumaColumn, x4 << 1);
                    transform = motionBlocks[row * sequence.MiColumns + column].Transform;
                }
                else
                {
                    transform = mode switch
                    {
                        0 or 3 or 13 => 0,
                        1 or 5 or 8 or 10 => 1,
                        2 or 6 or 7 or 11 => 2,
                        4 or 9 or 12 => 3,
                        _ => throw new InvalidDataException("The AV1 chroma prediction mode is invalid.")
                    };
                }
                if (size.Maximum == 3)
                {
                    if (inter && transform == 9) { return 9; }
                    return 0;
                }
                if (inter && header.ReducedTransformSet) { return transform == 9 ? 9 : 0; }
                if (!inter && (header.ReducedTransformSet || size.Minimum == 2) && transform > 3 && transform != 9) { return 0; }
                if (inter && size.Minimum == 2 && transform > 11) { return 0; }
                return transform;
            }
            if (header.Quantization.BaseIndex == 0) { return 0; }
            if (inter)
            {
                if (header.ReducedTransformSet || size.Maximum == 3)
                {
                    int type = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterTx3Offset + size.Minimum * 3, 3));
                    return type == 0 ? 9 : 0;
                }
                if (size.Minimum == 2)
                {
                    int type = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterTx2Offset, 13));
                    ReadOnlySpan<byte> transforms = [9, 10, 11, 0, 1, 2, 4, 5, 3, 6, 7, 8];
                    return transforms[type];
                }
                int symbol = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterTx1Offset + size.Minimum * 17, 17));
                ReadOnlySpan<byte> fullTransforms = [9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 4, 5, 3, 6, 7, 8];
                return fullTransforms[symbol];
            }
            if (size.Maximum == 3) { return 0; }
            if (header.ReducedTransformSet || size.Minimum == 2)
            {
                int symbol = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntraTx2Offset + (size.Minimum * 13 + mode) * 6, 6));
                ReadOnlySpan<byte> transforms = [9, 0, 3, 1, 2];
                return transforms[symbol];
            }
            int selected = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntraTx1Offset + (size.Minimum * 13 + mode) * 8, 8));
            ReadOnlySpan<byte> normalTransforms = [9, 0, 10, 11, 3, 1, 2];
            return normalTransforms[selected];
        }

        private void StoreTransform(int plane, int x4, int y4, Av1TransformSize size, int transform)
        {
            if (plane != 0 || motionBlocks.IsEmpty) { return; }
            for (int row = 0; row < size.Height4 && y4 + row < sequence.MiRows; row++)
            {
                for (int column = 0; column < size.Width4 && x4 + column < sequence.MiColumns; column++)
                {
                    motionBlocks[(y4 + row) * sequence.MiColumns + x4 + column].Transform = (byte)transform;
                }
            }
        }
    }
}
