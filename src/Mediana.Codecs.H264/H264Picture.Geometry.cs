// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public bool IsFieldMacroblock(int x, int y) => Layout.MacroblockAdaptiveFrameField
            ? Grid.IsField(Grid.Address(x, y)) : Layout.IsField;

        private (int Offset, int Stride) SampleLocation(int plane, int x, int y)
        {
            int stride = Stride(plane);
            if (!Layout.MacroblockAdaptiveFrameField)
            {
                return (y * stride + x, stride);
            }
            int horizontalScale = HorizontalScale(plane);
            int verticalScale = VerticalScale(plane);
            int width = 16 / horizontalScale;
            int height = 16 / verticalScale;
            int address = Grid.Address(x * horizontalScale, y * verticalScale);
            (int originX, int originY, int step) = Grid.PixelOrigin(address, width, height);
            return ((originY + y % height * step) * stride + originX + x % width, stride * step);
        }

        public H264NeighbourLocation? Neighbour(int plane, int x, int y, int offsetX, int offsetY)
        {
            int horizontalScale = HorizontalScale(plane);
            int verticalScale = VerticalScale(plane);
            int width = 16 / horizontalScale;
            int height = 16 / verticalScale;
            int address = Grid.Address(x * horizontalScale, y * verticalScale);
            return Grid.Neighbour(address, x % width + offsetX, y % height + offsetY, width, height, FirstMacroblock);
        }

        private int NeighbourBlockIndex(int plane, H264NeighbourLocation neighbour)
        {
            (int x, int y) = Grid.Position(neighbour.Address);
            return BlockIndex(plane, x / HorizontalScale(plane) + neighbour.X, y / VerticalScale(plane) + neighbour.Y);
        }

        private int MappedNeighbourCount(int plane, int x, int y)
        {
            H264NeighbourLocation? left = Neighbour(plane, x, y, -1, 0);
            H264NeighbourLocation? top = Neighbour(plane, x, y, 0, -1);
            Span<byte> counts = Counts(plane);
            int leftCount = left is H264NeighbourLocation l ? counts[NeighbourBlockIndex(plane, l)] : 0;
            int topCount = top is H264NeighbourLocation t ? counts[NeighbourBlockIndex(plane, t)] : 0;
            return left is null ? topCount : top is null ? leftCount : (leftCount + topCount + 1) >> 1;
        }

        public int MappedCodedBlockContext(int plane, int x, int y, bool intra)
        {
            int unavailable = intra ? 1 : 0;
            H264NeighbourLocation? left = Neighbour(plane, x, y, -1, 0);
            H264NeighbourLocation? top = Neighbour(plane, x, y, 0, -1);
            Span<byte> counts = Counts(plane);
            int leftFlag = left is H264NeighbourLocation l ? (counts[NeighbourBlockIndex(plane, l)] != 0 ? 1 : 0) : unavailable;
            int topFlag = top is H264NeighbourLocation t ? (counts[NeighbourBlockIndex(plane, t)] != 0 ? 1 : 0) : unavailable;
            return leftFlag + 2 * topFlag;
        }

        private int MappedPredictedMode(int x, int y, bool constrained)
        {
            if (Neighbour(0, x, y, -1, 0) is not H264NeighbourLocation left ||
                Neighbour(0, x, y, 0, -1) is not H264NeighbourLocation top)
            {
                return 2;
            }
            int leftMode = Modes[NeighbourBlockIndex(0, left)] & 127;
            int topMode = Modes[NeighbourBlockIndex(0, top)] & 127;
            if (leftMode == 127 || topMode == 127 || (constrained && (leftMode == InterMode || topMode == InterMode)))
            {
                return 2;
            }
            return Math.Min(leftMode == InterMode ? 2 : leftMode, topMode == InterMode ? 2 : topMode);
        }

        private (bool HasTop, bool HasLeft, TSample? Corner) ReadMappedNeighbours<TSample>(int plane, int x, int y,
            int width, int height, int topLength, Span<TSample> top, Span<TSample> left, bool constrained)
            where TSample : unmanaged
        {
            ReadOnlySpan<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(Plane(plane));
            bool hasTop = true;
            bool hasLeft = true;
            for (int index = 0; index < width; index++)
            {
                hasTop &= ReadNeighbour(pixels, plane, x, y, index, -1, constrained, out top[index]);
            }
            if (hasTop)
            {
                for (int index = width; index < topLength; index++)
                {
                    if (!ReadNeighbour(pixels, plane, x, y, index, -1, constrained, out top[index]))
                    {
                        top[index] = top[width - 1];
                    }
                }
            }
            for (int index = 0; index < height; index++)
            {
                hasLeft &= ReadNeighbour(pixels, plane, x, y, -1, index, constrained, out left[index]);
            }
            TSample? corner = ReadNeighbour(pixels, plane, x, y, -1, -1, constrained, out TSample sample) ? sample : null;
            return (hasTop, hasLeft, corner);
        }

        private bool ReadNeighbour<TSample>(ReadOnlySpan<TSample> pixels, int plane, int x, int y,
            int offsetX, int offsetY, bool constrained, out TSample sample) where TSample : unmanaged
        {
            sample = default;
            if (Neighbour(plane, x, y, offsetX, offsetY) is not H264NeighbourLocation neighbour)
            {
                return false;
            }
            int horizontalScale = HorizontalScale(plane);
            int verticalScale = VerticalScale(plane);
            H264NeighbourLocation luma = neighbour with { X = neighbour.X * horizontalScale, Y = neighbour.Y * verticalScale };
            byte mode = Modes[NeighbourBlockIndex(0, luma)];
            if (mode >= 128 || (constrained && mode == InterMode))
            {
                return false;
            }
            (int originX, int originY, int step) = Grid.PixelOrigin(neighbour.Address, 16 / horizontalScale, 16 / verticalScale);
            sample = pixels[(originY + neighbour.Y * step) * Stride(plane) + originX + neighbour.X];
            return true;
        }
    }
}
