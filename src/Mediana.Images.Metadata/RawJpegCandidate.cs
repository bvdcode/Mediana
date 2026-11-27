// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    internal class RawJpegCandidate(ImageInfo info, ReadOnlyMemory<byte> encoded, int? directoryOrientation, JpegColorSpace? colorSpace)
        : EncodedImagePreview(info, encoded)
    {
        internal int? DirectoryOrientation { get; } = directoryOrientation;
        internal JpegColorSpace? ColorSpace { get; } = colorSpace;
    }
}
