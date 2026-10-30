// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal static class PreviewColorPolicy
    {
        private const int MaximumSdWidth = 1024;
        private const int MaximumSdHeight = 576;
        private const int MaximumNtscHeight = 480;
        private static readonly VideoColorInfo Hd = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709);
        private static readonly VideoColorInfo Pal = new(ColorPrimaries.Bt470Bg, TransferFunction.Bt709, MatrixCoefficients.Bt601);
        private static readonly VideoColorInfo Ntsc = new(ColorPrimaries.Smpte170M, TransferFunction.Bt709, MatrixCoefficients.Bt601);
        private static readonly VideoColorInfo Wide = new(ColorPrimaries.Bt2020, TransferFunction.Bt709, MatrixCoefficients.Bt2020NonConstant);
        private static readonly VideoColorInfo Smpte = new(ColorPrimaries.Smpte170M, TransferFunction.Smpte240M, MatrixCoefficients.Smpte240M);

        internal static VideoColorInfo Resolve(VideoFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            VideoColorInfo declared = format.Color;
            if (declared.Transfer is TransferFunction.Pq or TransferFunction.Hlg)
            {
                throw new NotSupportedException("HDR preview requires a tone mapping operation.");
            }
            if (declared.Primaries != ColorPrimaries.Unspecified && declared.Transfer != TransferFunction.Unspecified &&
                declared.Matrix != MatrixCoefficients.Unspecified)
            {
                return declared;
            }
            VideoColorInfo defaults = SelectDefaults(format);
            VideoColorInfo resolved = declared;
            if (declared.Primaries == ColorPrimaries.Unspecified)
            {
                resolved = resolved with { Primaries = defaults.Primaries };
            }
            if (declared.Transfer == TransferFunction.Unspecified)
            {
                resolved = resolved with { Transfer = defaults.Transfer };
            }
            if (declared.Matrix == MatrixCoefficients.Unspecified)
            {
                resolved = resolved with { Matrix = defaults.Matrix };
            }
            return resolved;
        }

        private static VideoColorInfo SelectDefaults(VideoFormat format)
        {
            if (ReferenceEquals(format.PixelFormat, VideoPixelFormat.Rgba32))
            {
                return VideoColorInfo.Srgb;
            }
            return format.Color.Matrix switch
            {
                MatrixCoefficients.Bt709 => Hd,
                MatrixCoefficients.Bt601 => Sd(format.Height),
                MatrixCoefficients.Smpte240M => Smpte,
                MatrixCoefficients.Bt2020NonConstant or MatrixCoefficients.Bt2020Constant => Wide,
                MatrixCoefficients.Unspecified => FromPrimaries(format),
                MatrixCoefficients.Rgb or MatrixCoefficients.Fcc or MatrixCoefficients.YCgCo or MatrixCoefficients.Smpte2085 or
                    MatrixCoefficients.ChromaticityNonConstant or MatrixCoefficients.ChromaticityConstant or MatrixCoefficients.ICtCp or
                    MatrixCoefficients.IptC2 or MatrixCoefficients.YCgCoRe or MatrixCoefficients.YCgCoRo => Geometry(format),
                _ => throw new ArgumentOutOfRangeException(nameof(format), "Invalid source matrix coefficients.")
            };
        }

        private static VideoColorInfo FromPrimaries(VideoFormat format) => format.Color.Primaries switch
        {
            ColorPrimaries.Bt709 => Hd,
            ColorPrimaries.Bt470Bg => Pal,
            ColorPrimaries.Smpte170M => Ntsc,
            ColorPrimaries.Bt2020 => Wide,
            ColorPrimaries.Unspecified or ColorPrimaries.Bt470M or ColorPrimaries.Film or ColorPrimaries.Xyz or
                ColorPrimaries.DciP3 or ColorPrimaries.DisplayP3 or ColorPrimaries.Ebu3213 => Geometry(format),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Invalid source color primaries.")
        };

        private static VideoColorInfo Geometry(VideoFormat format) =>
            format.Width <= MaximumSdWidth && format.Height <= MaximumSdHeight ? Sd(format.Height) : Hd;

        private static VideoColorInfo Sd(int height) => height <= MaximumNtscHeight ? Ntsc : Pal;
    }
}
