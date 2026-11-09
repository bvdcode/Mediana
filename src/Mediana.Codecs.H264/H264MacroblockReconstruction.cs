// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264MacroblockReconstruction
    {
        public static long Prepare(H264SourceMacroblock source, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients, bool intra, Span<byte> reconstruction)
        {
            Span<int> residual = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            if (intra) { H264InverseTransform.LumaDc(coefficients.Slice(240, 16), qp, dc); }
            long error = 0;
            for (int index = 0; index < 16; index++)
            {
                (int bx, int by) = H264Picture.LumaBlockPosition(index);
                if (intra)
                {
                    H264InverseTransform.Residual4x4(coefficients.Slice(index * 15, 15), qp,
                        residual, dc[by / 4 * 4 + bx / 4]);
                }
                else if ((pattern & (1 << (index / 4))) != 0)
                { H264InverseTransform.Residual4x4(coefficients.Slice(index * 16, 16), qp, residual); }
                else { residual.Clear(); }
                error += BlockError(source.Luma, prediction[..256], 16, bx, by, residual, reconstruction[..256]);
            }
            int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
            int chromaPattern = pattern >> 4;
            for (int plane = 1; plane <= 2; plane++)
            {
                if (chromaPattern != 0)
                { H264InverseTransform.ChromaDc(coefficients.Slice(376 + (plane - 1) * 4, 4), chromaQp, dc[..4]); }
                else { dc.Clear(); }
                for (int block = 0; block < 4; block++)
                {
                    int bx = block % 2 * 4, by = block / 2 * 4;
                    if (chromaPattern != 0)
                    {
                        H264InverseTransform.Residual4x4(coefficients.Slice(256 + ((plane - 1) * 4 + block) * 15, 15),
                            chromaQp, residual, dc[block]);
                    }
                    else { residual.Clear(); }
                    error += BlockError(source.Chroma.Slice((plane - 1) * 64, 64),
                        prediction.Slice(256 + (plane - 1) * 64, 64), 8, bx, by, residual, reconstruction.Slice(256 + (plane - 1) * 64, 64));
                }
            }
            return error;
        }
        private static long BlockError(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction,
            int stride, int x, int y, ReadOnlySpan<int> residual, Span<byte> reconstruction)
            => H264SampleReconstruction.WriteAndMeasure(source[(y * stride + x)..], prediction[(y * stride + x)..], stride,
                residual, reconstruction[(y * stride + x)..]);
    }
}
