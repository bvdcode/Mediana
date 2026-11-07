// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal ref struct JpegSampleCursor
    {
        private readonly ReadOnlySpan<byte> samples;
        private readonly int stride;
        private readonly int width;
        private readonly int height;
        private readonly int dx;
        private readonly int dy;
        private readonly int stepX;
        private readonly int stepY;
        private readonly int divisor;
        private readonly int divisorShift;
        private readonly bool direct;
        private readonly int directStep;
        private int directOffset;
        private int lowX;
        private int lowY;
        private int fractionX;
        private int fractionY;
        private int upperRow;
        private int lowerRow;

        public JpegSampleCursor(scoped in JpegSampleLayout image, JpegComponent component, int x, int y,
            OrthogonalTransform orientation, int phaseX, int phaseY)
        {
            this = default;
            samples = component.Samples.Span;
            stride = component.SampleStride;
            width = component.SampleWidth;
            height = component.SampleHeight;
            direct = component.Horizontal == image.MaximumHorizontal && component.Vertical == image.MaximumVertical;
            directOffset = y * stride + x;
            directStep = orientation.A + orientation.C * stride;
            if (direct) { return; }
            dx = image.MaximumHorizontal * 2;
            dy = image.MaximumVertical * 2;
            stepX = 2 * orientation.A * component.Horizontal;
            stepY = 2 * orientation.C * component.Vertical;
            int nx = 2 * x * component.Horizontal + (phaseX == 0 ? 0 : component.Horizontal - phaseX);
            int ny = 2 * y * component.Vertical + (phaseY == 0 ? 0 : component.Vertical - phaseY);
            lowX = Math.DivRem(nx, dx, out fractionX);
            lowY = Math.DivRem(ny, dy, out fractionY);
            if (fractionX < 0) { lowX--; fractionX += dx; }
            if (fractionY < 0) { lowY--; fractionY += dy; }
            upperRow = Math.Clamp(lowY, 0, height - 1) * stride;
            lowerRow = Math.Clamp(lowY + 1, 0, height - 1) * stride;
            divisor = dx * dy;
            divisorShift = BitOperations.IsPow2(divisor) ? BitOperations.TrailingZeroCount(divisor) : -1;
        }

        public int ReadNext()
        {
            if (direct)
            {
                int sample = samples[directOffset];
                directOffset += directStep;
                return sample;
            }
            int x0 = Math.Clamp(lowX, 0, width - 1);
            int x1 = Math.Clamp(lowX + 1, 0, width - 1);
            int top = samples[upperRow + x0] * (dx - fractionX) + samples[upperRow + x1] * fractionX;
            int bottom = samples[lowerRow + x0] * (dx - fractionX) + samples[lowerRow + x1] * fractionX;
            int sum = top * (dy - fractionY) + bottom * fractionY + divisor / 2;
            int value = divisorShift >= 0 ? sum >> divisorShift : sum / divisor;
            fractionX += stepX;
            if (fractionX >= dx) { fractionX -= dx; lowX++; }
            else if (fractionX < 0) { fractionX += dx; lowX--; }
            fractionY += stepY;
            if (fractionY >= dy)
            {
                fractionY -= dy;
                lowY++;
                UpdateRows();
            }
            else if (fractionY < 0)
            {
                fractionY += dy;
                lowY--;
                UpdateRows();
            }
            return value;
        }

        private void UpdateRows()
        {
            upperRow = Math.Clamp(lowY, 0, height - 1) * stride;
            lowerRow = Math.Clamp(lowY + 1, 0, height - 1) * stride;
        }
    }
}
