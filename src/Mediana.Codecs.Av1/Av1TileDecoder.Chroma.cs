// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private Av1ChromaMode ReadChromaMode(int lumaMode, int blockIndex, int width, int height)
        {
            bool cfl = lossless
                ? Math.Max(1, width >> 1) == 1 && Math.Max(1, height >> 1) == 1
                : Math.Max(width, height) <= 8;
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
            return new(mode, angleDelta, alphaU, alphaV);
        }

        private void PredictChromaFromLuma(int plane, int x, int y, int alpha, int maximumX, int maximumY, int width, int height)
        {
            Span<int> luma = stackalloc int[width * height];
            int sum = 0;
            for (int row = 0; row < height; row++)
            {
                int sourceY = Math.Min((y + row) * 2, maximumY - 2);
                for (int column = 0; column < width; column++)
                {
                    int sourceX = Math.Min((x + column) * 2, maximumX - 2);
                    int top = sourceY * sequence.Stride + sourceX;
                    int bottom = top + sequence.Stride;
                    int value = (output[top] + output[top + 1] + output[bottom] + output[bottom + 1]) * 2;
                    luma[row * width + column] = value;
                    sum += value;
                }
            }
            int average = (sum + width * height / 2) / (width * height);
            int offset = sequence.PlaneOffset(plane);
            int stride = sequence.PlaneStride(plane);
            for (int row = 0; row < Math.Min(height, sequence.PlaneHeight(plane) - y); row++)
            {
                Span<byte> target = output.Slice(offset + (y + row) * stride + x, Math.Min(width, stride - x));
                for (int column = 0; column < target.Length; column++)
                {
                    int scaled = alpha * (luma[row * width + column] - average);
                    int magnitude = (Math.Abs(scaled) + 32) >> 6;
                    int delta = scaled < 0 ? -magnitude : magnitude;
                    target[column] = (byte)Math.Clamp(target[column] + delta, 0, 255);
                }
            }
        }
    }
}
