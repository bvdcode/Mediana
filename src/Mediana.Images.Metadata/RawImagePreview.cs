// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Images.Metadata
{
    /// <summary>An independently owned embedded JPEG and its containing image's TIFF orientation.</summary>
    public class RawImagePreview
    {
        private readonly JpegColorSpace? colorSpace;
        private readonly int requestedSize;

        internal RawImagePreview(byte[] encoded, int containerExifOrientation, JpegColorSpace? colorSpace, int requestedSize)
        {
            this.colorSpace = colorSpace;
            this.requestedSize = requestedSize;
            Encoded = encoded;
            ContainerExifOrientation = containerExifOrientation;
        }

        public ReadOnlyMemory<byte> Encoded { get; }
        public int ContainerExifOrientation { get; }

        /// <summary>Decodes the oriented JPEG at a native resolution suitable for the long edge
        /// requested at extraction. The caller can resize the returned frame to the exact output size.</summary>
        public VideoFrame DecodeForPreview(MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            Decode(allocator, new() { PreferredLongEdge = requestedSize }, cancellationToken);

        /// <summary>Decodes directly into display orientation. An explicit JPEG orientation takes precedence.</summary>
        public VideoFrame Decode(MediaBufferAllocator allocator, ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            JpegImageDecoder decoder = RawJpegPreviews.CreateDecoder(ContainerExifOrientation, colorSpace);
            return decoder.Decode(Encoded, request ?? new(), allocator, cancellationToken);
        }
    }
}
