// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Explicit RGB primary coordinates and white point; transfer and sample range remain in the frame's color labels.</summary>
    public class RgbChromaticitiesColorDescription : VideoColorDescription
    {
        public RgbChromaticitiesColorDescription(RgbChromaticities chromaticities)
        {
            chromaticities.Validate(); Chromaticities = chromaticities;
        }
        public RgbChromaticities Chromaticities { get; }
        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            if (Chromaticities == RgbChromaticities.Bt709) { return codecColor with { Primaries = ColorPrimaries.Bt709 }; }
            throw new NotSupportedException("Explicit RGB chromaticities require a color transform; they cannot be replaced by enumerated color labels.");
        }
    }
}
