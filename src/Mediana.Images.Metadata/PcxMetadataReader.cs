// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    public class PcxMetadataReader : ImageMetadataReader
    {
        public override string ImplementationId => "pcx.managed.metadata";
        public override int ProbeLength => 74;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 74
            && prefix[0] == 10 && prefix[1] is 0 or 2 or 3 or 4 or 5 && prefix[2] is 0 or 1
            && prefix[3] is 1 or 2 or 4 or 8 && prefix[65] is >= 1 and <= 4;

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> data = encoded.Span;
            if (data.Length < 128 || !CanRead(data)) { throw new InvalidDataException("Invalid PCX header."); }
            int left = Word(data, 4), top = Word(data, 6), right = Word(data, 8), bottom = Word(data, 10);
            int width = right - left + 1, height = bottom - top + 1, stride = Word(data, 66);
            if (width <= 0 || height <= 0 || stride == 0 || (stride & 1) != 0 || stride * 8 < width * data[3])
            {
                throw new InvalidDataException("Invalid PCX bounds or scanline stride.");
            }
            List<MediaProperty> properties = [new("pcx", "version", (int)data[1]), new("pcx", "encoding", (int)data[2]),
                new("pcx", "bits_per_plane", (int)data[3]), new("pcx", "planes", (int)data[65]), new("pcx", "bytes_per_line", stride),
                new("pcx", "origin", left, top), new("pcx", "horizontal_dpi", Word(data, 12)),
                new("pcx", "vertical_dpi", Word(data, 14)), new("pcx", "palette_info", Word(data, 68)),
                new("pcx", "screen_size", Word(data, 70), Word(data, 72))];
            if (data[1] is 2 or 5) { properties.Add(new("pcx", "header_palette_rgb", MediaPropertyValue.FromBytes(data.Slice(16, 48)))); }
            if (data[3] == 8 && data[65] == 1 && data.Length >= 128 + 769 && data[^769] == 12)
            {
                properties.Add(new("pcx", "palette_rgb", MediaPropertyValue.FromBytes(data[^768..])));
            }
            return new(new("pcx", width, height, ImageDimensionUnit.Pixels, metadata: new("image", properties)));
        }
        private static int Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }
}
