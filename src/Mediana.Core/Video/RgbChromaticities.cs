// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>CIE xy primary and white-point coordinates. Imaginary primaries are permitted.</summary>
    public readonly record struct RgbChromaticities(double RedX, double RedY, double GreenX, double GreenY,
        double BlueX, double BlueY, double WhiteX, double WhiteY)
    {
        public static RgbChromaticities Bt709 { get; } = new(0.640, 0.330, 0.300, 0.600, 0.150, 0.060, 0.3127, 0.3290);
        public void Validate()
        {
            if (!double.IsFinite(RedX) || !double.IsFinite(RedY) || !double.IsFinite(GreenX) || !double.IsFinite(GreenY)
                || !double.IsFinite(BlueX) || !double.IsFinite(BlueY) || !double.IsFinite(WhiteX) || !double.IsFinite(WhiteY)
                || RedY == 0 || GreenY == 0 || BlueY == 0 || WhiteY <= 0)
            { throw new ArgumentException("RGB chromaticities require finite coordinates and nonzero primary y values."); }
        }
    }
}
