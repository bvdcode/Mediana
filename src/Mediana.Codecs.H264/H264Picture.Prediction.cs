// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void PredictLuma4x4(int x, int y, int mode, Span<byte> output, bool constrained = false) =>
            PredictLuma(x, y, mode, output, constrained, 4);

        public void PredictLuma8x8(int x, int y, int mode, Span<byte> output, bool constrained) =>
            PredictLuma(x, y, mode, output, constrained, 8);

        private void PredictLuma(int x, int y, int mode, Span<byte> output, bool constrained, int size)
        {
            if (SampleBytes == 1)
            {
                PredictLuma<byte>(x, y, mode, output, constrained, size);
            }
            else
            {
                PredictLuma<ushort>(x, y, mode, MemoryMarshal.Cast<byte, ushort>(output), constrained, size);
            }
        }

        private void PredictLuma<TSample>(int x, int y, int mode, Span<TSample> output, bool constrained, int size)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            Span<TSample> top = stackalloc TSample[16];
            Span<TSample> left = stackalloc TSample[8];
            (bool hasTop, bool hasLeft, TSample? corner) = ReadLumaNeighbours(x, y, size, top, left, constrained);
            if (size == 8)
            {
                H264IntraPrediction<TSample>.Luma8x8(mode, hasTop ? top : [], hasLeft ? left : [], corner, output, MaximumSample);
            }
            else
            {
                H264IntraPrediction<TSample>.Luma4x4(mode, hasTop ? top[..8] : [], hasLeft ? left[..4] : [], corner, output, MaximumSample);
            }
        }

        public (bool HasTop, bool HasLeft, byte? Corner) ReadLumaNeighbours(int x, int y, int size,
            Span<byte> top, Span<byte> left, bool constrained = false) =>
            ReadLumaNeighbours<byte>(x, y, size, top, left, constrained);

        private (bool HasTop, bool HasLeft, TSample? Corner) ReadLumaNeighbours<TSample>(int x, int y, int size,
            Span<TSample> top, Span<TSample> left, bool constrained)
            where TSample : unmanaged
        {
            Span<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(Plane(0));
            bool hasTop = IsAvailable(x, y - 1, constrained);
            bool hasLeft = IsAvailable(x - 1, y, constrained);
            if (hasTop)
            {
                for (int index = 0; index < size * 2; index++)
                {
                    top[index] = IsAvailable(x + index, y - 1, constrained) ? pixels[(y - 1) * Width + x + index] : top[size - 1];
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

        private void PredictMacroblock<TSample>(int plane, int x, int y, int mode, Span<TSample> output, bool constrained)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int size = plane == 0 ? 16 : 8;
            int height = 16 / VerticalScale(plane);
            int stride = Stride(plane);
            Span<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(Plane(plane));
            Span<TSample> top = stackalloc TSample[16];
            Span<TSample> left = stackalloc TSample[16];
            int scale = plane == 0 ? 1 : 2;
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
            if (plane == 0)
            {
                H264IntraPrediction<TSample>.Luma16x16(mode, hasTop ? top : [], hasLeft ? left : [], corner, output, MaximumSample);
            }
            else
            {
                H264IntraPrediction<TSample>.Chroma(mode, hasTop ? top[..8] : [], hasLeft ? left[..height] : [], corner, output, height, MaximumSample);
            }
        }
    }
}
