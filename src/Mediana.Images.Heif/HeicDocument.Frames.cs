// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif.Container;

namespace Mediana.Images.Heif
{
    public partial class HeicDocument
    {
        internal static async Task<VideoFrame> DecodePrimaryFrameAsync(ReadOnlyMemory<byte> encoded,
            HeicDecodeOptions options, SampleAspectRatio sampleAspectRatio, MediaBufferAllocator allocator,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HeicDocument document = new(encoded, options);
            if (!document.metadata.TryGetValue(document.PrimaryItemId, out ImageProperties? properties))
            {
                throw new UnsupportedHeicFeatureException("primary image item type");
            }
            (int width, int height) = properties.GetDisplaySize(cancellationToken);
            using WritableMediaBuffer output = allocator.Allocate(checked(width * height * 4));
            ImageItemDecoder decoder = new(document.container, document.metadata, options, cancellationToken);
            RgbaImage image = await decoder.DecodeToAsync(document.PrimaryItemId, output.Memory).ConfigureAwait(false);
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, image.GetVideoColorInfo(), sampleAspectRatio);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = output.Publish();
            return new VideoFrame(format, buffer, format.CreateTightLayout());
        }
    }
}
