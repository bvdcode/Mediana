// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264FastIntraCandidate
    {
        private const int IntraExtraSignalingBits = 6;
        private const int ZeroMotionDifferenceBits = 2;

        public static bool TryWrite(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            int address, int x, int y, int qp, int temporalDifference, int? estimatedVectorBits, Span<int> coefficients)
        {
            int intraBudget = temporalDifference - SignalingDifference(qp, estimatedVectorBits);
            if (intraBudget <= 0)
            {
                return false;
            }

            Span<byte> intra = stackalloc byte[H264SourceMacroblock.ByteCount];
            int mode = H264Intra16ModeSearch.Select(source, target, x, y, intra[..256], out int luma);
            if (luma >= intraBudget)
            {
                return false;
            }
            int chromaMode = H264ChromaModeSearch.Select(target, x, y, source.Chroma, intra[256..], out int chroma);
            if (luma + chroma >= intraBudget)
            {
                return false;
            }
            bool lumaMatchesPrediction = luma == 0;
            if (!lumaMatchesPrediction)
            {
                target.PredictMacroblock(0, x, y, mode, intra[..256]);
            }
            syntax.WriteSkip(address, false);
            H264Intra16Encoder.WriteCabac(syntax, source, target, address, x, y, qp, mode, chromaMode, intra,
                coefficients, lumaMatchesPrediction);
            return true;
        }

        private static int SignalingDifference(int qp, int? estimatedVectorBits)
        {
            if (estimatedVectorBits is not int bits)
            {
                return 8 << (qp / 6);
            }
            int vectorOverhead = bits - ZeroMotionDifferenceBits;
            int sadPerBit = 1 << Math.Max((qp - 12) / 6, 0);
            return sadPerBit * (IntraExtraSignalingBits - vectorOverhead);
        }
    }
}
