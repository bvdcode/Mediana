// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers.Matroska
{
    public static class MatroskaStreamFormats
    {
        /// <summary>Original CodecPrivate and one stored frame after removal of Block headers and lacing.
        /// No content decoding, codec normalization or independently decodable boundary is asserted.</summary>
        public static EncodedStreamFormat StoredFrames { get; } = new(
            "matroska.codec-private", "matroska.stored-frame", EncodedPacketBoundary.ArbitraryBytes);
    }
}
