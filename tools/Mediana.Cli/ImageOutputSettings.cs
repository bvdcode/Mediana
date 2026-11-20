// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Cli
{
    internal record ImageOutputSettings(ImageCompressionMode? Compression = null, int? Quality = null, VideoChromaSampling? ChromaSampling = null);
}
