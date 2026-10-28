namespace Mediana.Codecs.H264
{
    internal class H264CabacIntraReader
    {
        private readonly H264CabacContext[] contexts;
        private H264CabacDecoder decoder;

        public H264CabacIntraReader(ref H264BitReader bits, H264SliceHeader header)
        {
            if (!header.Picture.Cabac || header.Kind != H264SliceKind.I)
            {
                throw new NotSupportedException("The CABAC intra reader requires an I slice with arithmetic entropy coding.");
            }
            bits.AlignOne();
            contexts = H264CabacIntraContexts.Create(header.Qp);
            decoder = new H264CabacDecoder(ref bits);
        }

        public bool Read(ref H264BitReader bits, int contextIndex) =>
            decoder.ReadBin(ref bits, ref contexts[H264CabacIntraContexts.GetIndex(contextIndex)]);

        public bool Bypass(ref H264BitReader bits) => decoder.ReadBypass(ref bits);
        public uint Bits(ref H264BitReader bits, int count) => decoder.ReadBypassBits(ref bits, count);
        public bool Termination(ref H264BitReader bits) => decoder.ReadTermination(ref bits);
        public void Restart(ref H264BitReader bits) => decoder = new H264CabacDecoder(ref bits);
    }
}
