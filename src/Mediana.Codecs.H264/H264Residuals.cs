namespace Mediana.Codecs.H264
{
    internal static class H264Residuals
    {
        public static void ReadChroma(ref H264BitReader bits, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction)
        {
            Span<int> coefficients = stackalloc int[15];
            Span<int> dcCoefficients = stackalloc int[8];
            Span<int> dc = stackalloc int[4];
            Span<int> residual = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16];
            dcCoefficients.Clear();
            if (pattern != 0)
            {
                H264Cavlc.Read(ref bits, dcCoefficients[..4], -1, 15);
                H264Cavlc.Read(ref bits, dcCoefficients[4..], -1, 15);
            }
            for (int plane = 1; plane <= 2; plane++)
            {
                int offset = plane == 1 ? picture.ChromaQpOffset : picture.SecondChromaQpOffset;
                int chromaQp = H264InverseTransform.ChromaQp(qp, offset);
                H264InverseTransform.ChromaDc(dcCoefficients.Slice((plane - 1) * 4, 4), chromaQp, dc);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = (index % 2) * 4;
                    int blockY = (index / 2) * 4;
                    coefficients.Clear();
                    int count = pattern == 2
                        ? H264Cavlc.Read(ref bits, coefficients, target.NeighbourCount(plane, x + blockX, y + blockY), 15) : 0;
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264InverseTransform.Residual4x4(coefficients, chromaQp, residual, dc[index]);
                    CopyPrediction(macroPrediction.Slice((plane - 1) * 64, 64), 8, blockX, blockY, prediction);
                    target.WriteBlock(plane, x + blockX, y + blockY, prediction, residual);
                }
            }
        }

        public static void CopyPrediction(ReadOnlySpan<byte> source, int stride, int x, int y, Span<byte> block)
        {
            for (int row = 0; row < 4; row++)
            {
                source.Slice((y + row) * stride + x, 4).CopyTo(block.Slice(row * 4, 4));
            }
        }
    }
}
