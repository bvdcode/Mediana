// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Intra16Encoder
    {
        public const int CoefficientCount = 256 + H264ChromaEncoder.CoefficientCount;

        public static bool TryWrite(H264BitWriter bits, H264SourceMacroblock source, H264Picture target, int x, int y,
            int qp, int previousQuantizer, int mode, H264SliceKind sliceKind)
        {
            Span<byte> prediction = stackalloc byte[384];
            Span<int> coefficients = stackalloc int[CoefficientCount];
            int pattern = Analyze(source, target, x, y, qp, mode, prediction, coefficients);
            int typeOffset = sliceKind switch
            {
                H264SliceKind.I => 0,
                H264SliceKind.P => 5,
                H264SliceKind.B or H264SliceKind.Sp or H264SliceKind.Si =>
                    throw new NotSupportedException("This intra encoder requires an I or P slice."),
                _ => throw new ArgumentOutOfRangeException(nameof(sliceKind))
            };
            bits.WriteUnsigned((uint)(typeOffset + 1 + mode + 4 * (pattern >> 4) + ((pattern & 15) != 0 ? 12 : 0)));
            bits.WriteUnsigned(0);
            H264MacroblockEncoding.WriteQuantizerDelta(bits, previousQuantizer, qp);
            return TryWrite(new H264CavlcEncodingSyntax(bits, target), target, x, y, qp, pattern, prediction, coefficients);
        }

        public static int Analyze(H264SourceMacroblock source, H264Picture target, int x, int y, int qp, int mode,
            Span<byte> prediction, Span<int> coefficients)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(mode);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(mode, 3);
            target.PredictMacroblock(0, x, y, mode, prediction[..256]);
            for (int plane = 1; plane <= 2; plane++)
            {
                target.PredictMacroblock(plane, x / 2, y / 2, 0, prediction.Slice(256 + (plane - 1) * 64, 64));
            }
            Span<int> luma = coefficients[..240];
            Span<int> rawDc = stackalloc int[16];
            for (int index = 0; index < 16; index += 2)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int offset = blockY * 16 + blockX;
                (int first, int second) = H264ForwardTransform.QuantizePair(source.Luma[offset..], 16,
                    prediction[offset..], 16, qp, luma.Slice(index * 15, 15), luma.Slice((index + 1) * 15, 15));
                int rasterIndex = blockY / 4 * 4 + blockX / 4;
                rawDc[rasterIndex] = first;
                rawDc[rasterIndex + 1] = second;
            }
            Span<int> dc = coefficients.Slice(240, 16);
            H264ForwardTransform.LumaDc(rawDc, qp, dc);
            int chroma = H264ChromaEncoder.Analyze(source.Chroma, qp, prediction[256..], coefficients[256..]);
            return (luma.ContainsAnyExcept(0) ? 15 : 0) | (chroma << 4);
        }

        public static bool TryWrite<TSyntax>(TSyntax syntax, H264Picture target, int x, int y, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients) where TSyntax : IH264EncodingSyntax
        {
            ReadOnlySpan<int> dc = coefficients.Slice(240, 16);
            if (!syntax.TryWriteResidual(H264ResidualCategory.Intra16LumaDc, 0, x, y, dc, out _))
            {
                return false;
            }
            Span<int> rawDc = stackalloc int[16];
            H264InverseTransform.LumaDc(dc, qp, rawDc);
            Span<byte> blockPrediction = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                ReadOnlySpan<int> levels = coefficients.Slice(index * 15, 15);
                int count = 0;
                if ((pattern & 15) != 0 && !syntax.TryWriteResidual(H264ResidualCategory.Intra16LumaAc,
                    0, x + blockX, y + blockY, levels, out count))
                {
                    return false;
                }
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
                target.SetLumaMode(x + blockX, y + blockY, 2);
                H264Residuals.CopyPrediction(prediction, 16, blockX, blockY, blockPrediction);
                H264InverseTransform.Residual4x4(levels, qp, residual, rawDc[blockY / 4 * 4 + blockX / 4]);
                target.WriteBlock(0, x + blockX, y + blockY, blockPrediction, residual);
            }
            target.Quantizers[y / 16 * target.Sequence.WidthInMbs + x / 16] = (byte)qp;
            return H264ChromaEncoder.TryWrite(syntax, target, x / 2, y / 2, qp, pattern >> 4, prediction[256..], coefficients[256..]);
        }
    }
}
