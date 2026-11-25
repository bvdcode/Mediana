// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264MacroblockReconstruction
    {
        public static long Prepare(H264SourceMacroblock source, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients, bool intra, Span<byte> reconstruction)
        {
            Span<int> residual = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            if (intra)
            {
                H264InverseTransform.LumaDc(coefficients.Slice(240, 16), qp, dc);
            }
            long error = 0;
            for (int index = 0; index < 16; index++)
            {
                (int bx, int by) = H264Picture.LumaBlockPosition(index);
                ReadOnlySpan<int> levels = intra ? coefficients.Slice(index * 15, 15) :
                    (pattern & (1 << (index / 4))) != 0 ? coefficients.Slice(index * 16, 16) : [];
                error += ReconstructBlock(source.Luma, prediction[..256], reconstruction[..256], 16,
                    bx, by, levels, qp, intra ? dc[by / 4 * 4 + bx / 4] : null, residual);
            }
            int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
            int chromaPattern = pattern >> 4;
            for (int plane = 0; plane < 2; plane++)
            {
                if (chromaPattern != 0)
                {
                    H264InverseTransform.ChromaDc(coefficients.Slice(376 + plane * 4, 4), chromaQp, dc[..4]);
                }
                ReadOnlySpan<byte> chromaSource = source.Chroma.Slice(plane * 64, 64);
                ReadOnlySpan<byte> chromaPrediction = prediction.Slice(256 + plane * 64, 64);
                Span<byte> chromaReconstruction = reconstruction.Slice(256 + plane * 64, 64);
                for (int block = 0; block < 4; block++)
                {
                    int bx = block % 2 * 4;
                    int by = block / 2 * 4;
                    ReadOnlySpan<int> levels = chromaPattern != 0 ? coefficients.Slice(256 + (plane * 4 + block) * 15, 15) : [];
                    error += ReconstructBlock(chromaSource, chromaPrediction, chromaReconstruction,
                        8, bx, by, levels, chromaQp, chromaPattern != 0 ? dc[block] : null, residual);
                }
            }
            return error;
        }
        private static long ReconstructBlock(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction,
            Span<byte> reconstruction, int stride, int x, int y, ReadOnlySpan<int> levels, int qp, int? dc, Span<int> residual)
        {
            int offset = y * stride + x;
            if (Vector128.IsHardwareAccelerated && H264InverseTransform.TryReconstructAndMeasure(
                levels, qp, dc, prediction[offset..], source[offset..], reconstruction[offset..], stride, out int error))
            {
                return error;
            }
            if (levels.IsEmpty && dc is null)
            {
                residual.Clear();
            }
            else
            {
                H264InverseTransform.Residual4x4(levels, qp, residual, dc);
            }
            return H264SampleReconstruction.WriteAndMeasure(source[offset..], prediction[offset..], stride, residual, reconstruction[offset..]);
        }
    }
}
