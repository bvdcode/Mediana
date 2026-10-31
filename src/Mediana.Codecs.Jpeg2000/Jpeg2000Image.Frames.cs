// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg2000
{
    public partial class Jpeg2000Image
    {
        /// <summary>Converts gray, sRGB or sYCC components to owned straight sRGB RGBA8.
        /// Native precision remains available through the component API. Raw codestreams require an explicit
        /// color space; ICC profiles are unsupported. The allocator counts only the converted output.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, SampleAspectRatio sampleAspectRatio,
            Jpeg2000ColorSpace? sourceColorSpace = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            Span<int> channels = stackalloc int[3];
            Jpeg2000ColorSpace colorSpace = PrepareRgbaConversion(sourceColorSpace, channels);
            VideoFormat format = new(Width, Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, sampleAspectRatio);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            Span<byte> output = writable.Memory.Span;
            WriteRgbaTo(output, colorSpace, channels, cancellationToken);
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout);
        }
    }
}
