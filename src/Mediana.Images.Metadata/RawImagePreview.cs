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

        internal RawImagePreview(byte[] encoded, int containerExifOrientation, JpegColorSpace? colorSpace)
        {
            this.colorSpace = colorSpace;
            Encoded = encoded;
            ContainerExifOrientation = containerExifOrientation;
        }

        public ReadOnlyMemory<byte> Encoded { get; }
        public int ContainerExifOrientation { get; }

        /// <summary>Decodes directly into display orientation. An explicit JPEG orientation takes precedence.</summary>
        public VideoFrame Decode(MediaBufferAllocator allocator, ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            JpegImageDecoder decoder = RawJpegPreviews.CreateDecoder(ContainerExifOrientation, colorSpace);
            return decoder.Decode(Encoded, request ?? new(), allocator, cancellationToken);
        }
    }
}
