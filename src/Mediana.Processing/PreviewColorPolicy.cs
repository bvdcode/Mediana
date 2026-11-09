// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Explicit preview interpretation for missing color labels. Uses the SDR policy for SDR input;
    /// PQ/HLG input retains its transfer, with BT.2020 defaults for omitted primaries and YUV matrix.
    /// HDR conversion additionally requires explicit tone mapping options.</summary>
    public static class PreviewColorPolicy
    {
        public static VideoColorInfo Resolve(VideoFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            VideoColorInfo declared = format.GetColorInterpretation();
            if (declared.Transfer is not (TransferFunction.Pq or TransferFunction.Hlg))
            { return SdrPreviewColorPolicy.Resolve(format); }
            VideoColorInfo resolved = VideoColorPolicy.ResolveChromaLocation(format);
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
