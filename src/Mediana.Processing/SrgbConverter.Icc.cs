// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        /// <summary>Applies an embedded ICC transform when present; otherwise resolves the source color labels
        /// with the supplied policy. The policy is never used to replace an embedded ICC profile.</summary>
        public static VideoFrame ApplyWithColorPolicy(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            NearestNeighborMap map = new(input.Format.Width, input.Format.Height, input.Format.Width, input.Format.Height,
                OrthogonalTransform.Identity);
            return ApplyWithColorPolicy(input, colorResolver, map, input.Format.SampleAspectRatio, allocator, cancellationToken);
        }

        internal static VideoFrame ApplyWithColorPolicy(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            NearestNeighborMap map, SampleAspectRatio? aspectRatio, MediaBufferAllocator allocator, CancellationToken token,
            HdrToneMappingOptions? toneMapping = null)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            token.ThrowIfCancellationRequested();
            if (input.Format.ColorDescription is IccColorDescription profile)
            {
                if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32) ||
                    input.Format.Color.Matrix != MatrixCoefficients.Rgb || input.Format.Color.Range != ColorRange.Full)
                {
                    throw new NotSupportedException("ICC conversion requires full-range straight RGBA32 samples.");
                }
                return ApplyRgba(input, map, aspectRatio, allocator, IccRgbTransform.Get(profile, token), token);
            }
            VideoColorInfo source = colorResolver?.Invoke(input.Format) ?? input.Format.GetColorInterpretation();
            if (toneMapping is not null && source.Transfer is TransferFunction.Pq or TransferFunction.Hlg)
            {
                return Apply(input, source, map, aspectRatio, allocator, new HdrRgbTransform(source.Primaries, source.Transfer, toneMapping), token);
            }
            return Apply(input, source, map, aspectRatio, allocator, token);
        }

        private static VideoFrame ApplyRgba<TTransform>(VideoFrame input, NearestNeighborMap map, SampleAspectRatio? aspectRatio,
            MediaBufferAllocator allocator, TTransform transform, CancellationToken token) where TTransform : IRgbaPixelTransform
        {
            VideoFormat format = new(map.OutputWidth, map.OutputHeight, VideoPixelFormat.Rgba32, SrgbColor, aspectRatio);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            RenderRgba(input, writable.Memory.Span, map, transform, token);
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes, input.Timestamp, input.Duration);
        }
    }
}
