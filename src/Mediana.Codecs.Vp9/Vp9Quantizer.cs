// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9Quantizer
    {
        internal static (int Dc, int Ac) Get(Vp9FrameHeader header, Vp9HeaderState state, int segment, int plane)
        {
            int quantizer = header.BaseQuantizer;
            if (header.Segmentation && state.SegmentEnabled[segment * 4])
            { quantizer = state.AbsoluteSegments ? state.SegmentFeatures[segment * 4] : quantizer + state.SegmentFeatures[segment * 4]; }
            quantizer = Math.Clamp(quantizer, 0, 255);
            int dcDelta = plane == 0 ? header.YDcDelta : header.UvDcDelta, acDelta = plane == 0 ? 0 : header.UvAcDelta;
            int depthOffset = (header.Geometry.BitDepth - 8) / 2 * 256;
            return (Vp9Tables.DcQuantizer[depthOffset + Math.Clamp(quantizer + dcDelta, 0, 255)],
                Vp9Tables.AcQuantizer[depthOffset + Math.Clamp(quantizer + acDelta, 0, 255)]);
        }
    }
}
