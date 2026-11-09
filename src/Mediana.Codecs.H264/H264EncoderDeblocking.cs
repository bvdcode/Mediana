// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264EncoderDeblocking
    {
        public static void WriteHeader(H264BitWriter bits, H264DeblockingOptions? options)
        {
            if (options is null)
            {
                bits.WriteUnsigned(1);
                return;
            }
            bits.WriteUnsigned(0);
            bits.WriteSigned(options.AlphaOffset);
            bits.WriteSigned(options.BetaOffset);
        }

        public static void Apply(H264Picture picture, H264PictureParameterSet parameters, H264Picture? reference,
            bool keyFrame, int quantizer, H264DeblockingOptions? options, CancellationToken cancellationToken)
        {
            if (options is null)
            {
                return;
            }
            H264SliceHeader header = new(picture.Sequence, parameters)
            {
                Kind = keyFrame ? H264SliceKind.I : H264SliceKind.P,
                IsIdr = keyFrame,
                Qp = quantizer,
                DeblockingIdc = 0,
                AlphaOffset = options.AlphaOffset * 2,
                BetaOffset = options.BetaOffset * 2
            };
            H264Deblocking.ApplySingleReference(picture, header, reference?.Identity, cancellationToken);
        }
    }
}
