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
            Func<VideoFormat, VideoColorInfo>? resolver = options.ColorResolver is null ? null : Resolve;
            if (preserveAspect && matrix == VideoDisplayMatrix.Identity &&
                input.Format.Width <= options.MaximumWidth && input.Format.Height <= options.MaximumHeight)
            { return SrgbConverter.ApplyWithColorPolicy(input, resolver, allocator, token); }
            return SrgbPreview.FitWithColorPolicy(input, resolver, matrix, options.MaximumWidth, options.MaximumHeight, allocator, token);

            VideoColorInfo Resolve(VideoFormat format)
            {
                return VideoColorPolicy.Resolve(format, options.ColorResolver!, options.WarningHandler);
            }
        }
    }
}
