// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1ChromaMode ReadChromaMode(int lumaMode, int blockIndex, int width, int height)
        {
            bool cfl = Math.Max(1, width >> 1) == 1 && Math.Max(1, height >> 1) == 1;
            int mode;
            if (cfl)
            {
                mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.UvModeCflOffset + lumaMode * 15, 15));
            }
            else
            {
                mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.UvModeOffset + lumaMode * 14, 14));
            }
            int alphaU = 0;
            int alphaV = 0;
            int angleDelta = 0;
            if (mode == 13)
            {
                int signs = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CflSignOffset, 9));
                int uSign = (signs + 1) / 3;
                int vSign = (signs + 1) % 3;
                if (uSign != 0)
                {
                    alphaU = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CflAlphaOffset + ((uSign - 1) * 3 + vSign) * 17, 17)) + 1;
                    if (uSign == 1) { alphaU = -alphaU; }
                }
                if (vSign != 0)
                {
                    alphaV = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CflAlphaOffset + ((vSign - 1) * 3 + uSign) * 17, 17)) + 1;
                    if (vSign == 1) { alphaV = -alphaV; }
                }
            }
            else if (mode is >= 1 and <= 8 && blockIndex >= 3)
            {
                angleDelta = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.AngleDeltaOffset + (mode - 1) * 8, 8)) - 3;
            }
            if (mode is >= 9 and <= 12)
            {
                throw new NotSupportedException("AV1 smooth and Paeth chroma prediction are not implemented.");
            }
            if (mode is >= 1 and <= 8 && sequence.IntraEdgeFilter && (mode != 1 && mode != 2 || angleDelta != 0))
            {
                throw new NotSupportedException("AV1 directional chroma edge filtering is not implemented.");
            }
            return new(mode, angleDelta, alphaU, alphaV);
        }

        private void PredictChromaFromLuma(int plane, int x, int y, int alpha, int maximumX, int maximumY)
        {
            Span<int> luma = stackalloc int[16];
            int sum = 0;
            for (int row = 0; row < 4; row++)
            {
                int sourceY = Math.Min((y + row) * 2, maximumY - 2);
                for (int column = 0; column < 4; column++)
                {
                    int sourceX = Math.Min((x + column) * 2, maximumX - 2);
                    int top = sourceY * sequence.Stride + sourceX;
                    int bottom = top + sequence.Stride;
                    int value = (output[top] + output[top + 1] + output[bottom] + output[bottom + 1]) * 2;
                    luma[row * 4 + column] = value;
                    sum += value;
                }
            }
            int average = (sum + 8) >> 4;
            int offset = sequence.PlaneOffset(plane);
            int stride = sequence.PlaneStride(plane);
            for (int row = 0; row < 4; row++)
            {
                Span<byte> target = output.Slice(offset + (y + row) * stride + x, 4);
                for (int column = 0; column < 4; column++)
                {
                    int scaled = alpha * (luma[row * 4 + column] - average);
                    int magnitude = (Math.Abs(scaled) + 32) >> 6;
                    int delta = scaled < 0 ? -magnitude : magnitude;
                    target[column] = (byte)Math.Clamp(target[column] + delta, 0, 255);
                }
            }
        }
    }
}
