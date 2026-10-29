// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Flac
{
    /// <summary>Declared picture metadata. Dimensions and media type do not validate the image.
    /// MediaType "-->" describes URI bytes; reading them does not retrieve the resource.</summary>
    public record FlacPictureInfo(uint PictureType, string MediaType, string Description,
        uint Width, uint Height, uint BitsPerPixel, uint Colors, int ByteLength);
}
