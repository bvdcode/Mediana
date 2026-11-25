// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264Intra16Reconstruction
    {
        public static void Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            int plane, int x, int y, int mode, int pattern, int qp, bool constrained)
            where TReader : IH264ResidualReader
        {
            Span<int> levels = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            int bitDepth = target.Sequence.LumaBitDepth;
            bool field = target.IsFieldMacroblock(x, y);
            int dcCount = reader.Read(ref bits, H264ResidualCategory.Intra16LumaDc, plane, x, y, levels);
            if (dcCount == 0)
            {
                dc.Clear();
            }
            else
            {
                H264InverseTransform.LumaDc(levels, qp, dc, bitDepth, field);
            }
            Span<byte> pixels = target.PredictIntra16InPlace(plane, x, y, mode, constrained, out int stride);
            if (pattern == 0)
            {
                if (dcCount != 0)
                {
                    AddDc(pixels, stride, target.SampleBytes, target.MaximumSample, dc);
                }
            }
            else
            {
                ReadBlocks(ref bits, reader, target, plane, x, y, pattern, qp, bitDepth, field, pixels, stride, levels[..15], dc);
            }
            target.FinishIntra16(plane, x, y, pattern == 0);
        }

        private static void ReadBlocks<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            int plane, int x, int y, int pattern, int qp, int bitDepth, bool field, scoped Span<byte> pixels,
            int stride, scoped Span<int> levels, scoped ReadOnlySpan<int> dc)
            where TReader : IH264ResidualReader
        {
            Span<byte> counts = target.Counts(plane);
            int countStride = target.Stride(plane) / 4;
            int start = target.BlockIndex(plane, x, y);
            int sampleBytes = target.SampleBytes;
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                levels.Clear();
                bool coded = (pattern & (1 << (index / 4))) != 0;
                int count = coded
                    ? reader.Read(ref bits, H264ResidualCategory.Intra16LumaAc, plane, x + blockX, y + blockY, levels)
                    : 0;
                counts[start + blockY / 4 * countStride + blockX / 4] = (byte)count;
                int scaledDc = dc[blockY + blockX / 4];
                if (count == 0 && scaledDc == 0)
                {
                    continue;
                }
                Span<byte> block = pixels[((blockY * stride + blockX) * sampleBytes)..];
                if (sampleBytes == 1 && Vector128.IsHardwareAccelerated && !field &&
                    H264InverseTransform.TryReconstructFrame4x4(levels, qp, scaledDc, block, stride, block, stride))
                {
                    continue;
                }
                H264InverseTransform.Residual4x4(levels, qp, residual, scaledDc, bitDepth, field);
                if (sampleBytes == 1)
                {
                    H264SampleReconstruction.Write(block, stride, residual, 4, block, stride);
                }
                else
                {
                    Span<ushort> samples = MemoryMarshal.Cast<byte, ushort>(block);
                    H264HighPrecisionReconstruction.Write(samples, stride, residual, 4, samples, stride, target.MaximumSample);
                }
            }
        }

        private static void AddDc(Span<byte> pixels, int stride, int sampleBytes, int maximum, ReadOnlySpan<int> dc)
        {
            if (sampleBytes != 1)
            {
                AddDcScalar(MemoryMarshal.Cast<byte, ushort>(pixels), stride, maximum, dc);
                return;
            }
            if (!Vector128.IsHardwareAccelerated)
            {
                AddDcScalar(pixels, stride, maximum, dc);
                return;
            }
            for (int row = 0; row < 16; row += 4)
            {
                int d0 = RoundDc(dc[row]);
                int d1 = RoundDc(dc[row + 1]);
                int d2 = RoundDc(dc[row + 2]);
                int d3 = RoundDc(dc[row + 3]);
                if ((d0 | d1 | d2 | d3) == 0)
                {
                    continue;
                }
                Vector128<byte> add = Vector128.Create(Repeat(d0), Repeat(d1), Repeat(d2), Repeat(d3)).AsByte();
                Vector128<byte> subtract = Vector128.Create(Repeat(-d0), Repeat(-d1), Repeat(-d2), Repeat(-d3)).AsByte();
                for (int line = row; line < row + 4; line++)
                {
                    Span<byte> destination = pixels[(line * stride)..];
                    Vector128<byte> prediction = MemoryMarshal.Read<Vector128<byte>>(destination);
                    Vector128<byte> result = Vector128.AddSaturate(Vector128.SubtractSaturate(prediction, subtract), add);
                    MemoryMarshal.Write(destination, in result);
                }
            }
        }

        private static void AddDcScalar<TSample>(Span<TSample> pixels, int stride, int maximum, ReadOnlySpan<int> dc)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            for (int row = 0; row < 16; row++)
            {
                for (int column = 0; column < 16; column++)
                {
                    int index = row * stride + column;
                    int delta = RoundDc(dc[row / 4 * 4 + column / 4]);
                    int predicted = int.CreateTruncating(pixels[index]);
                    pixels[index] = TSample.CreateTruncating(Math.Clamp(predicted + delta, 0, maximum));
                }
            }
        }

        private static int RoundDc(int scaledDc) => (int)(((long)scaledDc + 32) >> 6);
        private static uint Repeat(int delta) => (uint)Math.Clamp(delta, 0, byte.MaxValue) * 0x01010101u;
    }
}
