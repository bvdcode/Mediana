// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264FastIntraCandidate
    {
        public static bool TryWrite(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            int address, int x, int y, int qp, int temporalDifference, Span<int> coefficients)
        {
            // Scale the intra signalling penalty in SAD units with the quantization step.
            int overhead = 8 << (qp / 6);
            if (temporalDifference <= overhead)
            {
                return false;
            }

            Span<byte> intra = stackalloc byte[H264SourceMacroblock.ByteCount];
            int mode = H264Intra16ModeSearch.Select(source, target, x, y, intra[..256], out int luma);
            if (luma + overhead >= temporalDifference)
            {
                return false;
            }
            int chromaMode = H264ChromaModeSearch.Select(target, x, y, source.Chroma, intra[256..], out int chroma);
            if (luma + chroma + overhead >= temporalDifference)
            {
                return false;
            }
            target.PredictMacroblock(0, x, y, mode, intra[..256]);
            syntax.WriteSkip(address, false);
            H264Intra16Encoder.WriteCabac(syntax, source, target, address, x, y, qp, mode, chromaMode, intra, coefficients);
            return true;
        }
    }
}
