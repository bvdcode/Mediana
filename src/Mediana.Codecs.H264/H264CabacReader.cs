// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264CabacReader
    {
        private readonly H264CabacContext[] contexts;
        private H264CabacDecoder decoder;

        public H264CabacReader(ref H264BitReader bits, H264SliceHeader header)
        {
            if (!header.Picture.Cabac)
            {
                throw new NotSupportedException("The CABAC reader requires arithmetic entropy coding.");
            }
            contexts = H264CabacDecodingContexts.Create(header);
            bits.AlignOne();
            decoder = new H264CabacDecoder(ref bits);
        }

        public bool Read(ref H264BitReader bits, int contextIndex) =>
            decoder.ReadBin(ref bits, ref contexts[contextIndex]);

        public bool Bypass(ref H264BitReader bits) => decoder.ReadBypass(ref bits);
        public uint Bits(ref H264BitReader bits, int count) => decoder.ReadBypassBits(ref bits, count);
        public bool Termination(ref H264BitReader bits) => decoder.ReadTermination(ref bits);
        public void Restart(ref H264BitReader bits) => decoder = new H264CabacDecoder(ref bits);
    }
}
