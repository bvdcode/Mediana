namespace Mediana.Codecs.H264
{
    internal readonly struct H264CavlcResidualReader(H264Picture target) : IH264ResidualReader
    {
        public int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients) =>
            H264Cavlc.Read(ref bits, coefficients,
                category == H264ResidualCategory.ChromaDc ? -1 : target.NeighbourCount(plane, x, y), 15);
    }
}
