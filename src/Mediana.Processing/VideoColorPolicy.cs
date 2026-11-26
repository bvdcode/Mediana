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
            if (format.PixelFormat is not PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Yuv })
            {
                return declared;
            }
            if (format.ScanMode != VideoScanMode.Progressive && declared.FieldChromaLocations is VideoFieldChromaLocations fields)
            {
                ChromaLocation assumed = declared.ChromaLocation;
                if (assumed == ChromaLocation.Unspecified)
                {
                    assumed = ChromaLocation.Left;
                }
                if (format.ScanMode != VideoScanMode.BottomField && fields.Top == ChromaLocation.Unspecified)
                {
                    fields = fields with { Top = assumed };
                }
                if (format.ScanMode != VideoScanMode.TopField && fields.Bottom == ChromaLocation.Unspecified)
                {
                    fields = fields with { Bottom = assumed };
                }
                return declared with { FieldChromaLocations = fields };
            }
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
            if (declared.Range == ColorRange.Unspecified && resolved.Range != ColorRange.Unspecified) { missing.Add("range"); }
            if (declared.ChromaLocation == ChromaLocation.Unspecified && resolved.ChromaLocation != ChromaLocation.Unspecified) { missing.Add("chroma location"); }
            if (declared.FieldChromaLocations != resolved.FieldChromaLocations)
            {
                missing.Add("field chroma locations");
            }
            bool preservesDeclarations =
                (declared.Primaries == ColorPrimaries.Unspecified || declared.Primaries == resolved.Primaries) &&
                (declared.Transfer == TransferFunction.Unspecified || declared.Transfer == resolved.Transfer) &&
                (declared.Matrix == MatrixCoefficients.Unspecified || declared.Matrix == resolved.Matrix) &&
                (declared.ChromaLocation == ChromaLocation.Unspecified || declared.ChromaLocation == resolved.ChromaLocation) &&
                (declared.Range == ColorRange.Unspecified || declared.Range == resolved.Range) && PreservesFields(declared.FieldChromaLocations, resolved.FieldChromaLocations);
            if (missing.Count == 0 || !preservesDeclarations)
            {
                warningHandler($"The configured color policy uses {resolved}. Source metadata was not modified.");
            }
            else
            {
                string range = declared.Range != resolved.Range ? $", range {resolved.Range}" : string.Empty;
                string fields = string.Empty;
                if (declared.FieldChromaLocations != resolved.FieldChromaLocations)
                {
                    fields = $", field chroma locations {resolved.FieldChromaLocations}";
                }
                warningHandler($"Missing source color metadata ({string.Join(", ", missing)}); processing uses primaries {resolved.Primaries}, transfer {resolved.Transfer}, matrix {resolved.Matrix}{range}, chroma location {resolved.ChromaLocation}{fields}. Declared values were preserved.");
            }
            return resolved;
        }

        private static bool PreservesFields(VideoFieldChromaLocations? declared, VideoFieldChromaLocations? resolved)
        {
            if (declared is not VideoFieldChromaLocations source)
            {
                return true;
            }
            return resolved is VideoFieldChromaLocations target &&
                (source.Top == ChromaLocation.Unspecified || source.Top == target.Top) &&
                (source.Bottom == ChromaLocation.Unspecified || source.Bottom == target.Bottom);
        }
    }
}
