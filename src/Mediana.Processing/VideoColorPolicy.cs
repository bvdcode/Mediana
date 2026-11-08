// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Applies an explicitly selected color interpretation and reports changed labels without modifying source metadata.</summary>
    public static class VideoColorPolicy
    {
        /// <summary>Interprets missing chroma siting for planar YUV without replacing colorimetry or requiring SDR input.</summary>
        public static VideoColorInfo ResolveChromaLocation(VideoFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            VideoColorInfo declared = format.GetColorInterpretation();
            if (declared.ChromaLocation == ChromaLocation.Unspecified &&
                format.PixelFormat is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv })
            {
                return declared with { ChromaLocation = ChromaLocation.Left };
            }
            return declared;
        }

        /// <summary>Preserves native YUV colorimetry, including HDR labels, while resolving missing siting.
        /// RGB input uses the explicit SDR interpretation needed before RGB-to-YUV conversion.</summary>
        public static VideoColorInfo ResolveForEncoding(VideoFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            if (format.PixelFormat is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv })
            {
                return ResolveChromaLocation(format);
            }
            return SdrPreviewColorPolicy.Resolve(format);
        }

        public static VideoColorInfo Resolve(VideoFormat format, Func<VideoFormat, VideoColorInfo> resolver, Action<string>? warningHandler = null)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(resolver);
            VideoColorInfo declared = format.GetColorInterpretation();
            VideoColorInfo resolved = resolver(format);
            ArgumentNullException.ThrowIfNull(resolved);
            if (declared == resolved || warningHandler is null) { return resolved; }
            List<string> missing = [];
            if (declared.Primaries == ColorPrimaries.Unspecified && resolved.Primaries != ColorPrimaries.Unspecified) { missing.Add("primaries"); }
            if (declared.Transfer == TransferFunction.Unspecified && resolved.Transfer != TransferFunction.Unspecified) { missing.Add("transfer"); }
            if (declared.Matrix == MatrixCoefficients.Unspecified && resolved.Matrix != MatrixCoefficients.Unspecified) { missing.Add("matrix"); }
            if (declared.ChromaLocation == ChromaLocation.Unspecified && resolved.ChromaLocation != ChromaLocation.Unspecified) { missing.Add("chroma location"); }
            bool preservesDeclarations =
                (declared.Primaries == ColorPrimaries.Unspecified || declared.Primaries == resolved.Primaries) &&
                (declared.Transfer == TransferFunction.Unspecified || declared.Transfer == resolved.Transfer) &&
                (declared.Matrix == MatrixCoefficients.Unspecified || declared.Matrix == resolved.Matrix) &&
                (declared.ChromaLocation == ChromaLocation.Unspecified || declared.ChromaLocation == resolved.ChromaLocation) &&
                declared.Range == resolved.Range;
            if (missing.Count == 0 || !preservesDeclarations)
            {
                warningHandler($"The configured color policy uses {resolved}. Source metadata was not modified.");
            }
            else
            {
                warningHandler($"Missing source color metadata ({string.Join(", ", missing)}); processing uses primaries {resolved.Primaries}, transfer {resolved.Transfer}, matrix {resolved.Matrix}, chroma location {resolved.ChromaLocation}. Declared values were preserved.");
            }
            return resolved;
        }
    }
}
