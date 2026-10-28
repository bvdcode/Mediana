namespace Mediana.Codecs.H264
{
    internal class H264CabacReader
    {
        private readonly H264CabacContext[] contexts;
        private readonly H264SliceKind kind;
        private H264CabacDecoder decoder;

        public H264CabacReader(ref H264BitReader bits, H264SliceHeader header)
        {
            if (!header.Picture.Cabac)
            {
                throw new NotSupportedException("The CABAC reader requires arithmetic entropy coding.");
            }
            kind = header.Kind;
            contexts = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.Create(header.Qp),
                H264SliceKind.P => H264CabacPredictiveContexts.Create(header.Qp, header.CabacInitIdc),
                _ => throw new NotSupportedException("The CABAC reader supports I and P slices.")
            };
            bits.AlignOne();
            decoder = new H264CabacDecoder(ref bits);
        }

        public bool Read(ref H264BitReader bits, int contextIndex)
        {
            int index = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.GetIndex(contextIndex),
                H264SliceKind.P => H264CabacPredictiveContexts.GetIndex(contextIndex),
                _ => throw new InvalidOperationException("The CABAC reader has an unsupported slice kind.")
            };
            return decoder.ReadBin(ref bits, ref contexts[index]);
        }

        public bool Bypass(ref H264BitReader bits) => decoder.ReadBypass(ref bits);
        public uint Bits(ref H264BitReader bits, int count) => decoder.ReadBypassBits(ref bits, count);
        public bool Termination(ref H264BitReader bits) => decoder.ReadTermination(ref bits);
        public void Restart(ref H264BitReader bits) => decoder = new H264CabacDecoder(ref bits);
    }
}
