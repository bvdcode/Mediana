// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    internal static class ImageMetadataScopes
    {
        internal static MediaMetadata WithPreviews(IEnumerable<MediaProperty> properties, IEnumerable<MediaMetadata> children,
            IReadOnlyList<EncodedImagePreview> previews)
        {
            List<MediaMetadata> scopes = [.. children];
            for (int i = 0; i < previews.Count; i++)
            {
                EncodedImagePreview preview = previews[i];
                scopes.Add(new("preview/" + i, [new("image", "index", i), new("image", "format", preview.Info.FormatId),
                    new("image", "width", preview.Info.Width), new("image", "height", preview.Info.Height),
                    new("image", "encoded_bytes", preview.Encoded.Length)], [preview.Info.Metadata]));
            }
            return new("image", properties, scopes);
        }
    }
}
