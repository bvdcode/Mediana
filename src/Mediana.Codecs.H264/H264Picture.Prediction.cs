// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void PredictLuma4x4(int x, int y, int mode, Span<byte> output, bool constrained = false, int plane = 0) =>
            PredictLuma(x, y, mode, output, constrained, 4, plane);

        public void PredictLuma8x8(int x, int y, int mode, Span<byte> output, bool constrained, int plane = 0) =>
            PredictLuma(x, y, mode, output, constrained, 8, plane);

        private void PredictLuma(int x, int y, int mode, Span<byte> output, bool constrained, int size, int plane)
        {
            if (SampleBytes == 1)
            {
                PredictLuma<byte>(x, y, mode, output, constrained, size, plane);
            }
            else
            {
                PredictLuma<ushort>(x, y, mode, MemoryMarshal.Cast<byte, ushort>(output), constrained, size, plane);
            }
        }

        private void PredictLuma<TSample>(int x, int y, int mode, Span<TSample> output, bool constrained, int size, int plane)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            Span<TSample> top = stackalloc TSample[16];
            Span<TSample> left = stackalloc TSample[8];
            (bool hasTop, bool hasLeft, TSample? corner) = ReadLumaNeighbours(x, y, size, top, left, constrained, plane);
            if (size == 8)
            {
                H264IntraPrediction<TSample>.Luma8x8(mode, hasTop ? top : [], hasLeft ? left : [], corner, output, MaximumSample);
            }
            else
            {
                H264IntraPrediction<TSample>.Luma4x4(mode, hasTop ? top[..8] : [], hasLeft ? left[..4] : [], corner, output, MaximumSample);
            }
        }

        public void ReconstructSimpleIntra8x8(int plane, int x, int y, int mode, bool constrained, int delta, bool uncoded)
        {
            (int offset, int stride) = SampleLocation(plane, x, y);
            Span<byte> pixels = Plane(plane)[(offset * SampleBytes)..];
            if (SampleBytes == 1)
            {
                ReconstructSimpleIntra8x8<byte>(plane, x, y, mode, constrained, delta, pixels, stride);
            }
            else
            {
                ReconstructSimpleIntra8x8<ushort>(plane, x, y, mode, constrained, delta,
                    MemoryMarshal.Cast<byte, ushort>(pixels), stride);
            }
            if (uncoded)
            {
                ClearBlockCounts(plane, x, y, 8, 8);
            }
            MarkReconstructed(BlockIndex(plane, x, y), Stride(plane) / 4, 2);
        }

        private void ReconstructSimpleIntra8x8<TSample>(int plane, int x, int y, int mode, bool constrained,
            int delta, Span<TSample> pixels, int stride) where TSample : unmanaged, IBinaryInteger<TSample>
        {
            Span<TSample> top = stackalloc TSample[16];
            Span<TSample> left = stackalloc TSample[8];
            (bool hasTop, bool hasLeft, TSample? corner) = ReadLumaNeighbours(x, y, 8, top, left, constrained, plane);
            H264IntraPrediction<TSample>.WriteSimple8x8(mode, hasTop ? top : [], hasLeft ? left : [], corner,
                pixels, stride, MaximumSample, delta);
        }

        public (bool HasTop, bool HasLeft, byte? Corner) ReadLumaNeighbours(int x, int y, int size,
            Span<byte> top, Span<byte> left, bool constrained = false) =>
            ReadLumaNeighbours<byte>(x, y, size, top, left, constrained);

        private (bool HasTop, bool HasLeft, TSample? Corner) ReadLumaNeighbours<TSample>(int x, int y, int size,
            Span<TSample> top, Span<TSample> left, bool constrained, int plane = 0)
            where TSample : unmanaged
        {
            if (Layout.MacroblockAdaptiveFrameField)
            {
                return ReadMappedNeighbours(plane, x, y, size, size, size * 2, top, left, constrained);
            }
            Span<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(Plane(plane));
            bool hasTop = IsAvailable(x, y - 1, constrained);
            bool hasLeft = IsAvailable(x - 1, y, constrained);
            if (hasTop)
            {
                int length = size * 2;
                int topRow = (y - 1) * Width + x;
                // Reconstruction and intra availability are stored per 4x4 block.
                for (int index = 0; index < length;)
                {
                    int count = Math.Min(4 - ((x + index) & 3), length - index);
                    Span<TSample> group = top.Slice(index, count);
                    if (index == 0 || IsAvailable(x + index, y - 1, constrained))
                    {
                        pixels.Slice(topRow + index, count).CopyTo(group);
                    }
                    else
                    {
                        group.Fill(top[size - 1]);
                    }
                    index += count;
                }
            }
            if (hasLeft)
            {
                for (int index = 0; index < size; index++)
                {
                    left[index] = pixels[(y + index) * Width + x - 1];
                }
            }
            TSample? corner = IsAvailable(x - 1, y - 1, constrained) ? pixels[(y - 1) * Width + x - 1] : null;
            return (hasTop, hasLeft, corner);
        }

        public void PredictMacroblock(int plane, int x, int y, int mode, Span<byte> output, bool constrained = false)
        {
            if (SampleBytes == 1)
            {
                PredictMacroblock<byte>(plane, x, y, mode, output, constrained);
            }
            else
            {
                PredictMacroblock<ushort>(plane, x, y, mode, MemoryMarshal.Cast<byte, ushort>(output), constrained);
            }
        }

        internal bool TryReadLuma16Neighbours(int x, int y, Span<byte> top, Span<byte> left, out byte corner)
        {
            (bool hasTop, bool hasLeft, byte? topLeft) = ReadMacroblockNeighbours(0, x, y, 16, 16, top, left, false);
            corner = topLeft.GetValueOrDefault();
            return hasTop && hasLeft && topLeft.HasValue;
        }

        private void PredictMacroblock<TSample>(int plane, int x, int y, int mode, Span<TSample> output, bool constrained)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int size = 16 / HorizontalScale(plane);
            int height = 16 / VerticalScale(plane);
            Span<TSample> top = stackalloc TSample[16];
            Span<TSample> left = stackalloc TSample[16];
            (bool hasTop, bool hasLeft, TSample? corner) = ReadMacroblockNeighbours(plane, x, y, size, height, top, left, constrained);
            if (plane == 0 || FullResolutionChroma)
            {
                H264IntraPrediction<TSample>.Luma16x16(mode, hasTop ? top : [], hasLeft ? left : [], corner, output, MaximumSample);
            }
            else
            {
                H264IntraPrediction<TSample>.Chroma(mode, hasTop ? top[..8] : [], hasLeft ? left[..height] : [], corner, output, height, MaximumSample);
            }
        }

        private (bool HasTop, bool HasLeft, TSample? Corner) ReadMacroblockNeighbours<TSample>(int plane, int x, int y,
            int size, int height, Span<TSample> top, Span<TSample> left, bool constrained) where TSample : unmanaged
        {
            if (Layout.MacroblockAdaptiveFrameField)
            {
                return ReadMappedNeighbours(plane, x, y, size, height, size, top, left, constrained);
            }
            int stride = Stride(plane);
            Span<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(Plane(plane));
            int scale = HorizontalScale(plane);
            int verticalScale = VerticalScale(plane);
            bool hasTop = IsAvailable(x * scale, y * verticalScale - 1, constrained);
            bool hasLeft = IsAvailable(x * scale - 1, y * verticalScale, constrained);
            if (hasTop)
            {
                pixels.Slice((y - 1) * stride + x, size).CopyTo(top);
            }
            if (hasLeft)
            {
                for (int index = 0; index < height; index++)
                {
                    left[index] = pixels[(y + index) * stride + x - 1];
                }
            }
            TSample? corner = IsAvailable(x * scale - 1, y * verticalScale - 1, constrained) ? pixels[(y - 1) * stride + x - 1] : null;
            return (hasTop, hasLeft, corner);
        }
    }
}
