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
            token.ThrowIfCancellationRequested();
            bool fit = !preserveAspect || matrix != VideoDisplayMatrix.Identity ||
                input.Format.Width > options.MaximumWidth || input.Format.Height > options.MaximumHeight;
            if (fit && input.Format.SampleAspectRatio is null && options.UnspecifiedSampleAspectRatio is SampleAspectRatio assumed)
            {
                assumed.Validate();
                VideoFormat format = input.Format;
                using VideoFrame interpreted = input.WithFormat(new(format.Width, format.Height, format.PixelFormat,
                    format.Color, assumed, format.ColorDescription));
                options.WarningHandler?.Invoke($"Missing sample aspect ratio; processing uses {assumed.Numerator}:{assumed.Denominator}. Source metadata was not modified.");
                return ApplyResolved(interpreted, matrix, preserveAspect, options, allocator, token);
            }
            return ApplyResolved(input, matrix, preserveAspect, options, allocator, token);
        }

        private static VideoFrame ApplyResolved(VideoFrame input, VideoDisplayMatrix matrix, bool preserveAspect,
            VisualExportOptions options, MediaBufferAllocator allocator, CancellationToken token)
        {
            Func<VideoFormat, VideoColorInfo>? resolver = options.ColorResolver is null && options.ToneMapping is null ? null : Resolve;
            if (preserveAspect && matrix == VideoDisplayMatrix.Identity &&
                input.Format.Width <= options.MaximumWidth && input.Format.Height <= options.MaximumHeight)
            {
                if (options.ToneMapping is HdrToneMappingOptions toneMapping)
                { return SrgbConverter.ApplyWithColorPolicy(input, resolver, toneMapping, allocator, token); }
                return SrgbConverter.ApplyWithColorPolicy(input, resolver, allocator, token);
            }
            if (options.ToneMapping is HdrToneMappingOptions previewToneMapping)
            { return SrgbPreview.FitWithColorPolicy(input, resolver, previewToneMapping, matrix, options.MaximumWidth, options.MaximumHeight, allocator, token); }
            return SrgbPreview.FitWithColorPolicy(input, resolver, matrix, options.MaximumWidth, options.MaximumHeight, allocator, token);

            VideoColorInfo Resolve(VideoFormat format)
            {
                VideoColorInfo color = options.ColorResolver is null ? format.GetColorInterpretation()
                    : VideoColorPolicy.Resolve(format, options.ColorResolver, options.WarningHandler);
                if (options.ToneMapping is HdrToneMappingOptions mapping && color.Transfer is TransferFunction.Pq or TransferFunction.Hlg)
                {
                    options.WarningHandler?.Invoke($"HDR {color.Transfer} preview uses Hable tone mapping with a {mapping.SourcePeakLuminance:G} cd/m² source peak and {mapping.TargetPeakLuminance:G} cd/m² target peak. Source metadata was not modified.");
                }
                return color;
            }
        }
    }
}
