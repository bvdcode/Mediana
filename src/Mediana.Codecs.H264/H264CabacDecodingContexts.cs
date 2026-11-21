// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Places decoding states at their normative context indices for direct arithmetic-bin access.</summary>
    internal static class H264CabacDecodingContexts
    {
        public static H264CabacContext[] Create(H264SliceHeader header)
        {
            int qp = new H264Quantizer(header.Sequence.LumaBitDepth).ContextValue(header.Qp);
            H264SliceKind kind = header.Kind;
            H264CabacContext[] main = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.Create(qp),
                H264SliceKind.P or H264SliceKind.B => H264CabacPredictiveContexts.Create(qp, header.CabacInitIdc),
                _ => throw new NotSupportedException("The CABAC reader supports I, P and B slices.")
            };
            bool fullResolution = header.Sequence.ChromaFormat == 3;
            H264CabacContext[] contexts = new H264CabacContext[fullResolution ? 1024 : 460];
            switch (kind)
            {
                case H264SliceKind.I:
                    main.AsSpan(0, 8).CopyTo(contexts.AsSpan(3));
                    main.AsSpan(8, 216).CopyTo(contexts.AsSpan(60));
                    main.AsSpan(224, 3).CopyTo(contexts.AsSpan(399));
                    break;
                case H264SliceKind.P:
                case H264SliceKind.B:
                    main.AsSpan(0, 13).CopyTo(contexts.AsSpan(11));
                    main.AsSpan(13, 236).CopyTo(contexts.AsSpan(40));
                    main.AsSpan(249, 3).CopyTo(contexts.AsSpan(399));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(header));
            }
            if (kind == H264SliceKind.B)
            {
                H264CabacBidirectionalContexts.Create(qp, header.CabacInitIdc).CopyTo(contexts, 24);
            }
            H264CabacContext[] transform = H264CabacTransformContexts.Create(qp, kind, header.CabacInitIdc);
            transform.CopyTo(contexts, 402);
            H264CabacContext[] field = [];
            if (!header.Sequence.FrameMbsOnly || fullResolution)
            {
                field = H264CabacFieldContexts.Create(qp, kind, header.CabacInitIdc);
                field.AsSpan(0, 122).CopyTo(contexts.AsSpan(277));
                field.AsSpan(122).CopyTo(contexts.AsSpan(436));
            }
            if (fullResolution)
            {
                H264CabacColourContexts.Initialize(contexts.AsSpan(H264CabacColourContexts.First), kind, main, transform, field);
            }
            return contexts;
        }
    }
}
