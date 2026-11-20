// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264CabacReader
    {
        private readonly H264CabacContext[] contexts;
        private readonly H264CabacContext[] transformContexts;
        private readonly H264CabacContext[] bidirectionalContexts;
        private readonly H264CabacContext[] fieldContexts;
        private readonly H264SliceKind kind;
        private H264CabacDecoder decoder;

        public H264CabacReader(ref H264BitReader bits, H264SliceHeader header)
        {
            if (!header.Picture.Cabac)
            {
                throw new NotSupportedException("The CABAC reader requires arithmetic entropy coding.");
            }
            kind = header.Kind;
            int contextQp = new H264Quantizer(header.Sequence.LumaBitDepth).ContextValue(header.Qp);
            contexts = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.Create(contextQp),
                H264SliceKind.P or H264SliceKind.B => H264CabacPredictiveContexts.Create(contextQp, header.CabacInitIdc),
                _ => throw new NotSupportedException("The CABAC reader supports I, P and B slices.")
            };
            bidirectionalContexts = kind switch
            {
                H264SliceKind.B => H264CabacBidirectionalContexts.Create(contextQp, header.CabacInitIdc),
                H264SliceKind.I or H264SliceKind.P => [],
                _ => throw new NotSupportedException("The CABAC reader supports I, P and B slices.")
            };
            transformContexts = H264CabacTransformContexts.Create(contextQp, kind, header.CabacInitIdc);
            fieldContexts = header.Sequence.FrameMbsOnly ? [] : H264CabacFieldContexts.Create(contextQp, kind, header.CabacInitIdc);
            bits.AlignOne();
            decoder = new H264CabacDecoder(ref bits);
        }

        public bool Read(ref H264BitReader bits, int contextIndex)
        {
            if (contextIndex is >= 277 and <= 398 or >= 436 and <= 459)
            {
                return decoder.ReadBin(ref bits, ref fieldContexts[H264CabacFieldContexts.GetIndex(contextIndex)]);
            }
            if (kind == H264SliceKind.B && contextIndex is >= 24 and <= 39)
            {
                return decoder.ReadBin(ref bits, ref bidirectionalContexts[contextIndex - 24]);
            }
            if (contextIndex is >= 402 and <= 435)
            {
                return decoder.ReadBin(ref bits, ref transformContexts[contextIndex - 402]);
            }
            int index = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.GetIndex(contextIndex),
                H264SliceKind.P => H264CabacPredictiveContexts.GetIndex(contextIndex),
                H264SliceKind.B when contextIndex >= 40 => H264CabacPredictiveContexts.GetIndex(contextIndex),
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
