// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264IntraLumaEncoder
    {
        public static int Analyze(H264BitWriter bits, ReadOnlySpan<byte> source, H264Picture target, int x, int y,
            int qp, Span<int> coefficients)
            => Analyze(new H264CavlcEncodingSyntax(bits, target), source, target, x, y, qp, coefficients);

        public static int Analyze<TSyntax>(TSyntax syntax, ReadOnlySpan<byte> source, H264Picture target, int x, int y,
            int qp, Span<int> coefficients) where TSyntax : IH264EncodingSyntax
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
                ReadOnlySpan<byte> blockSource = source[(blockY * 16 + blockX)..];
                int mode = H264IntraModeSelection.Select(blockSource, target, px, py, prediction);
                syntax.WriteIntraMode(mode, predicted);
                target.SetLumaMode(px, py, mode);
                H264EncodingBlocks.Transform(blockSource, 16, prediction, 4, transformed);
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

        public static bool TryWrite(H264BitWriter bits, H264Picture target, int x, int y, int pattern, ReadOnlySpan<int> coefficients)
            => TryWrite(new H264CavlcEncodingSyntax(bits, target), target, x, y, pattern, coefficients);

        public static bool TryWrite<TSyntax>(TSyntax syntax, H264Picture target, int x, int y, int pattern,
            ReadOnlySpan<int> coefficients) where TSyntax : IH264EncodingSyntax
        {
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int count = 0;
                if ((pattern & (1 << (index / 4))) != 0 && !syntax.TryWriteResidual(H264ResidualCategory.Luma4x4,
                    0, x + blockX, y + blockY, coefficients.Slice(index * 16, 16), out count))
                {
                    return false;
                }
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
            }
            return true;
        }
    }
}
