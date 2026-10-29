// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class RgbPrimariesTransform
    {
        private const double WhiteX = 0.3127;
        private const double WhiteY = 0.3290;
        private static readonly RgbMatrix XyzToSrgb = ToXyz(0.640, 0.330, 0.300, 0.600, 0.150, 0.060).Inverse();

        public static RgbMatrix ToSrgb(ColorPrimaries primaries)
        {
            // Rec. ITU-T H.264, Table E-3. These primaries share the D65 white point.
            RgbMatrix source = primaries switch
            {
                ColorPrimaries.Bt709 => ToXyz(0.640, 0.330, 0.300, 0.600, 0.150, 0.060),
                ColorPrimaries.Bt470Bg => ToXyz(0.640, 0.330, 0.290, 0.600, 0.150, 0.060),
                ColorPrimaries.Smpte170M => ToXyz(0.630, 0.340, 0.310, 0.595, 0.155, 0.070),
                ColorPrimaries.Bt2020 => ToXyz(0.708, 0.292, 0.170, 0.797, 0.131, 0.046),
                ColorPrimaries.DisplayP3 => ToXyz(0.680, 0.320, 0.265, 0.690, 0.150, 0.060),
                ColorPrimaries.Ebu3213 => ToXyz(0.630, 0.340, 0.295, 0.605, 0.155, 0.077),
                ColorPrimaries.Unspecified or ColorPrimaries.Bt470M or ColorPrimaries.Film or ColorPrimaries.Xyz or
                    ColorPrimaries.DciP3 => throw new NotSupportedException("The source color primaries are unspecified or not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(primaries))
            };
            if (primaries == ColorPrimaries.Bt709)
            {
                return RgbMatrix.Identity;
            }
            return XyzToSrgb.Multiply(source);
        }

        private static RgbMatrix ToXyz(double rx, double ry, double gx, double gy, double bx, double by)
        {
            RgbMatrix basis = new(rx / ry, gx / gy, bx / by, 1, 1, 1,
                (1 - rx - ry) / ry, (1 - gx - gy) / gy, (1 - bx - by) / by);
            (double red, double green, double blue) = basis.Inverse().Transform(WhiteX / WhiteY, 1, (1 - WhiteX - WhiteY) / WhiteY);
            return basis.ScaleColumns(red, green, blue);
        }
    }
}
