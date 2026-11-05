// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static class WebpMetadataWriter
    {
        internal static int Length(WebpMetadata? metadata) => metadata is null ? 0 : checked(
            ChunkLength(metadata.IccProfile) + ChunkLength(metadata.Exif) + ChunkLength(metadata.Xmp));

        internal static int ChunkProfileLength(WebpMetadata? metadata) => metadata is null ? 0 : ChunkLength(metadata.IccProfile);
        internal static int DescriptionsLength(WebpMetadata? metadata) => metadata is null ? 0 : checked(ChunkLength(metadata.Exif) + ChunkLength(metadata.Xmp));

        internal static byte Flags(WebpMetadata? metadata)
        {
            if (metadata is null) { return 0; }
            return (byte)((metadata.IccProfile.IsEmpty ? 0 : 32) | (metadata.Exif.IsEmpty ? 0 : 8) | (metadata.Xmp.IsEmpty ? 0 : 4));
        }

        internal static int WriteProfile(Span<byte> output, WebpMetadata? metadata) =>
            WriteChunk(output, "ICCP"u8, metadata?.IccProfile ?? ReadOnlyMemory<byte>.Empty);

        internal static int WriteDescriptions(Span<byte> output, WebpMetadata? metadata)
        {
            int length = WriteChunk(output, "EXIF"u8, metadata?.Exif ?? ReadOnlyMemory<byte>.Empty);
            return checked(length + WriteChunk(output[length..], "XMP "u8, metadata?.Xmp ?? ReadOnlyMemory<byte>.Empty));
        }

        private static int ChunkLength(ReadOnlyMemory<byte> data) => data.IsEmpty ? 0 : WebpRiff.ChunkLength(data.Length);

        private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> name, ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty) { return 0; }
            WebpRiff.WriteChunk(output, name, data.Length);
            data.Span.CopyTo(output[WebpRiff.ChunkHeaderBytes..]);
            if ((data.Length & 1) != 0) { output[WebpRiff.ChunkHeaderBytes + data.Length] = 0; }
            return WebpRiff.ChunkLength(data.Length);
        }
    }
}
