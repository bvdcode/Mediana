// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Reads the optional visual-object video-signal fields without decoding picture samples.</summary>
    public static class Mpeg4VisualSignal
    {
        public static VideoColorInfo Read(ReadOnlySpan<byte> payload)
        {
            Mpeg4VisualBitReader bits = new(payload);
            if (bits.Flag()) { bits.Skip(7); }
            int objectType = bits.Read(4);
            if (objectType is not 1 and not 2) { throw new NotSupportedException("The MPEG-4 visual object is not a video or still texture."); }
            VideoColorInfo color = new(Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Left);
            if (!bits.Flag()) { return color; }
            bits.Skip(3); ColorRange range = bits.Flag() ? ColorRange.Full : ColorRange.Limited;
            if (!bits.Flag()) { return color with { Range = range }; }
            return new(CicpColorimetry.GetPrimaries(bits.Read(8)), CicpColorimetry.GetTransfer(bits.Read(8)),
                CicpColorimetry.GetMatrix(bits.Read(8)), range, ChromaLocation.Left);
        }
    }
}
