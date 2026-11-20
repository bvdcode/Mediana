// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    public class ImageInspection
    {
        public ImageInspection(ImageInfo info, IEnumerable<EncodedImagePreview>? previews = null)
        {
            ArgumentNullException.ThrowIfNull(info);
            Info = info;
            EncodedImagePreview[] copy = previews?.ToArray() ?? [];
            if (copy.Any(item => item is null)) { throw new ArgumentException("Image previews cannot contain null.", nameof(previews)); }
            Previews = Array.AsReadOnly(copy);
        }
        public ImageInfo Info { get; }
        public IReadOnlyList<EncodedImagePreview> Previews { get; }
    }
}
