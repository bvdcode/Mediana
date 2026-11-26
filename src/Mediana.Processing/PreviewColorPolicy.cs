// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Explicit preview interpretation for missing color labels. Uses the SDR policy for SDR input;
    /// PQ/HLG input retains its transfer, with BT.2020 defaults for omitted primaries and YUV matrix.
    /// Unspecified SDR YUV range is interpreted as limited. HDR conversion requires tone mapping options.</summary>
    public static class PreviewColorPolicy
    {
        public static VideoColorInfo Resolve(VideoFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            VideoColorInfo declared = format.GetColorInterpretation();
            bool hdr = declared.Transfer is TransferFunction.Pq or TransferFunction.Hlg;
            VideoColorInfo resolved = hdr
                ? VideoColorPolicy.ResolveChromaLocation(format)
                : SdrPreviewColorPolicy.Resolve(format);
            if (!hdr && resolved.Range == ColorRange.Unspecified &&
                format.PixelFormat is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv })
            {
                resolved = resolved with { Range = ColorRange.Limited };
            }
            if (!hdr) { return resolved; }
            if (resolved.Primaries == ColorPrimaries.Unspecified) { resolved = resolved with { Primaries = ColorPrimaries.Bt2020 }; }
            if (resolved.Matrix == MatrixCoefficients.Unspecified)
            {
                MatrixCoefficients matrix = format.PixelFormat is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv }
                    ? MatrixCoefficients.Bt2020NonConstant : MatrixCoefficients.Rgb;
                resolved = resolved with { Matrix = matrix };
            }
            return resolved;
        }
    }
}
