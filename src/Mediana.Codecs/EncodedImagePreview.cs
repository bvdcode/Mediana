// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs
{
    /// <summary>Original encoded bytes of an embedded image, borrowed from its source. Info describes the preview, not sensor dimensions.</summary>
    public class EncodedImagePreview
    {
        public EncodedImagePreview(ImageInfo info, ReadOnlyMemory<byte> encoded)
        {
            ArgumentNullException.ThrowIfNull(info);
            if (encoded.IsEmpty) { throw new ArgumentException("An encoded preview cannot be empty.", nameof(encoded)); }
            Info = info;
            Encoded = encoded;
        }
        public ImageInfo Info { get; }
        public ReadOnlyMemory<byte> Encoded { get; }
    }
}
