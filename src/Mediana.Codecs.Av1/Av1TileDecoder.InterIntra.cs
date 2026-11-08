// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1InterIntraPrediction ReadInterIntra(int width, int height)
        {
            int blockIndex = BlockIndex(width, height);
            if (!sequence.Tools.InterIntra || blockIndex is < 3 or > 9) { return default; }
            int context = BlockSizeGroup(blockIndex) - 1;
            if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterIntraOffset + context * 3, 3)) == 0) { return default; }
            int mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterIntraBlendModeOffset + context * 5, 5));
            bool wedge = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.WedgeInterIntraOffset + blockIndex * 3, 3)) != 0;
            int wedgeIndex = wedge ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.WedgeIndexOffset + blockIndex * 17, 17)) : -1;
            return new(true, mode, wedgeIndex);
        }

        private void BlendInterIntra(int row, int column, int width, int height, bool hasChroma, Av1InterIntraPrediction prediction)
        {
            Span<byte> inter = stackalloc byte[width * height * 16];
            Span<byte> mask = prediction.WedgeIndex >= 0 ? stackalloc byte[width * height * 16] : default;
            if (!mask.IsEmpty) { Av1WedgeMask.Initialize(mask, width * 4, height * 4, prediction.WedgeIndex, 0); }
            for (int plane = 0; plane < (hasChroma ? 3 : 1); plane++)
            {
                int sub = sequence.PlaneShift(plane);
                int planeWidth = (width * 4) >> sub;
                int planeHeight = (height * 4) >> sub;
                int x = (column * 4) >> sub;
                int y = (row * 4) >> sub;
                int stride = sequence.PlaneStride(plane);
                int visibleWidth = Math.Min(planeWidth, stride - x);
                int visibleHeight = Math.Min(planeHeight, sequence.PlaneHeight(plane) - y);
                int offset = sequence.PlaneOffset(plane) + y * stride + x;
                for (int dy = 0; dy < visibleHeight; dy++)
                {
                    output.Slice(offset + dy * stride, visibleWidth).CopyTo(inter.Slice(dy * visibleWidth, visibleWidth));
                }
                PredictIntra(plane, x, y, prediction.Intra, planeWidth, planeHeight, row, column);
                for (int dy = 0; dy < visibleHeight; dy++)
                {
                    Span<byte> target = output.Slice(offset + dy * stride, visibleWidth);
                    for (int dx = 0; dx < visibleWidth; dx++)
                    {
                        int weight = mask.IsEmpty ? Av1InterIntraMask.Weight(prediction.Mode, planeWidth, planeHeight, dy, dx)
                            : Av1MaskSampling.Weight(mask, width * 4, sub, dy, dx);
                        target[dx] = (byte)((weight * target[dx] + (64 - weight) * inter[dy * visibleWidth + dx] + 32) >> 6);
                    }
                }
            }
        }
    }
}
