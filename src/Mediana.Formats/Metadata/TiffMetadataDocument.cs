// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Formats.Metadata
{
    internal record TiffMetadataDocument(TiffDirectoryReader Reader, ImageInfo Info);
}
