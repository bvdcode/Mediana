// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana
{
    internal static class VisualFramePreparation
    {
        public static VideoFrame Apply(VideoFrame input, VideoDisplayMatrix matrix, bool preserveAspect,
            VisualExportOptions options, MediaBufferAllocator allocator, CancellationToken token)
        {
            Func<VideoFormat, VideoColorInfo>? resolver = options.ColorResolver is null ? null : Resolve;
            if (preserveAspect && matrix == VideoDisplayMatrix.Identity &&
                input.Format.Width <= options.MaximumWidth && input.Format.Height <= options.MaximumHeight)
            { return SrgbConverter.ApplyWithColorPolicy(input, resolver, allocator, token); }
            return SrgbPreview.FitWithColorPolicy(input, resolver, matrix, options.MaximumWidth, options.MaximumHeight, allocator, token);

            VideoColorInfo Resolve(VideoFormat format)
            {
                VideoColorInfo declared = format.GetColorInterpretation();
                VideoColorInfo resolved = options.ColorResolver!(format);
                if (declared != resolved)
                {
                    List<string> missing = [];
                    if (declared.Primaries == ColorPrimaries.Unspecified) { missing.Add("primaries"); }
                    if (declared.Transfer == TransferFunction.Unspecified) { missing.Add("transfer"); }
                    if (declared.Matrix == MatrixCoefficients.Unspecified) { missing.Add("matrix"); }
                    bool preservesDeclarations =
                        (declared.Primaries == ColorPrimaries.Unspecified || declared.Primaries == resolved.Primaries) &&
                        (declared.Transfer == TransferFunction.Unspecified || declared.Transfer == resolved.Transfer) &&
                        (declared.Matrix == MatrixCoefficients.Unspecified || declared.Matrix == resolved.Matrix) &&
                        declared.Range == resolved.Range && declared.ChromaLocation == resolved.ChromaLocation;
                    if (missing.Count == 0 || !preservesDeclarations)
                    { options.WarningHandler?.Invoke($"The configured color policy uses {resolved}. Source metadata was not modified."); }
                    else { options.WarningHandler?.Invoke($"Missing source color metadata ({string.Join(", ", missing)}); preview uses primaries {resolved.Primaries}, transfer {resolved.Transfer}, matrix {resolved.Matrix}. Declared values were preserved."); }
                }
                return resolved;
            }
        }
    }
}
