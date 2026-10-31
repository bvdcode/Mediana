// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraLumaEncoder
    {
        public static int Analyze(H264BitWriter bits, VideoFrame input, H264Picture target, int x, int y,
            int qp, Span<int> coefficients)
        {
            Span<byte> prediction = stackalloc byte[16];
            Span<int> transformed = stackalloc int[16];
            Span<int> residual = stackalloc int[16];
            int pattern = 0;
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int px = x + blockX;
                int py = y + blockY;
                int predicted = target.PredictedMode(px, py);
                int mode = H264IntraModeSelection.Select(input, target, px, py, prediction);
                bits.WriteFlag(mode == predicted);
                if (mode != predicted)
                {
                    bits.WriteBits((uint)(mode < predicted ? mode : mode - 1), 3);
                }
                target.SetLumaMode(px, py, mode);
                H264EncodingBlocks.Transform(input, 0, px, py, prediction, transformed);
                Span<int> levels = coefficients.Slice(index * 16, 16);
                H264ForwardTransform.Quantize4x4(transformed, qp, levels);
                if (levels.ContainsAnyExcept(0))
                {
                    pattern |= 1 << (index / 4);
                }
                H264InverseTransform.Residual4x4(levels, qp, residual);
                target.WriteBlock(0, px, py, prediction, residual);
            }
            return pattern;
        }

        public static void Write(H264BitWriter bits, H264Picture target, int x, int y, int pattern, ReadOnlySpan<int> coefficients)
        {
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int count = (pattern & (1 << (index / 4))) != 0
                    ? H264CavlcWriter.Write(bits, coefficients.Slice(index * 16, 16), target.NeighbourCount(0, x + blockX, y + blockY)) : 0;
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
            }
        }
    }
}
