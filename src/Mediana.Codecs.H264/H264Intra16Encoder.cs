// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Intra16Encoder
    {
        public static bool TryWrite(H264BitWriter bits, H264SourceMacroblock source, H264Picture target, int x, int y,
            int qp, int previousQuantizer, int mode, H264SliceKind sliceKind)
        {
            Span<byte> prediction = stackalloc byte[384];
            target.PredictMacroblock(0, x, y, mode, prediction[..256]);
            for (int plane = 1; plane <= 2; plane++)
            {
                target.PredictMacroblock(plane, x / 2, y / 2, 0, prediction.Slice(256 + (plane - 1) * 64, 64));
            }
            Span<int> luma = stackalloc int[240];
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
            Span<int> dc = stackalloc int[16];
            H264ForwardTransform.LumaDc(rawDc, qp, dc);
            Span<int> chroma = stackalloc int[H264ChromaEncoder.CoefficientCount];
            int chromaPattern = H264ChromaEncoder.Analyze(source.Chroma, qp, prediction[256..], chroma);
            bool hasAc = luma.ContainsAnyExcept(0);
            int typeOffset = sliceKind switch
            {
                H264SliceKind.I => 0,
                H264SliceKind.P => 5,
                H264SliceKind.B or H264SliceKind.Sp or H264SliceKind.Si =>
                    throw new NotSupportedException("This intra encoder requires an I or P slice."),
                _ => throw new ArgumentOutOfRangeException(nameof(sliceKind))
            };
            bits.WriteUnsigned((uint)(typeOffset + 1 + mode + 4 * chromaPattern + (hasAc ? 12 : 0)));
            bits.WriteUnsigned(0);
            H264MacroblockEncoding.WriteQuantizerDelta(bits, previousQuantizer, qp);
            if (!H264CavlcWriter.TryWrite(bits, dc, target.NeighbourCount(0, x, y), out _))
            {
                return false;
            }
            H264InverseTransform.LumaDc(dc, qp, rawDc);
            Span<byte> blockPrediction = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                ReadOnlySpan<int> levels = luma.Slice(index * 15, 15);
                int count = 0;
                if (hasAc && !H264CavlcWriter.TryWrite(bits, levels, target.NeighbourCount(0, x + blockX, y + blockY), out count))
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
            return H264ChromaEncoder.TryWrite(bits, target, x / 2, y / 2, qp, chromaPattern, prediction[256..], chroma);
        }
    }
}
