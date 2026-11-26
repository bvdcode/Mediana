// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    /// <summary>Reads Fujifilm RAF sensor declarations and the original embedded JPEG without decoding samples.</summary>
    public class RafMetadataReader : ImageMetadataReader
    {
        public override string ImplementationId => "raf.managed.metadata";
        public override int ProbeLength => 16;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("FUJIFILMCCD-RAW "u8);

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span) || encoded.Length < 108) { throw new InvalidDataException("Invalid RAF header."); }
            ReadOnlySpan<byte> header = encoded.Span;
            uint jpegOffset = BinaryPrimitives.ReadUInt32BigEndian(header[84..]);
            uint jpegLength = BinaryPrimitives.ReadUInt32BigEndian(header[88..]);
            uint directoryOffset = BinaryPrimitives.ReadUInt32BigEndian(header[92..]);
            uint directoryLength = BinaryPrimitives.ReadUInt32BigEndian(header[96..]);
            RawJpegPreviews previews = new(cancellationToken);
            if (jpegOffset != 0 || jpegLength != 0) { previews.Add(Slice(encoded, jpegOffset, jpegLength)); }
            List<MediaProperty> properties = [new("raw", "family", "raf"),
                new("raf", "format_version", Encoding.ASCII.GetString(header.Slice(16, 4))),
                new("raf", "camera_model", Encoding.ASCII.GetString(header.Slice(28, 32)).TrimEnd('\0'))];
            ImageMetadataCursor directory = new(Slice(encoded, directoryOffset, directoryLength));
            uint entries = directory.Dword();
            if (entries > directory.Remaining / 4) { throw new InvalidDataException("RAF directory count exceeds its extent."); }
            uint width = 0, height = 0;
            HashSet<ushort> seen = [];
            for (uint i = 0; i < entries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort tag = directory.Word(); ReadOnlyMemory<byte> value = directory.Take(directory.Word());
                if (!seen.Add(tag)) { throw new InvalidDataException("Duplicate RAF metadata tag."); }
                if (tag is 256 or 272 or 273 or 277)
                {
                    if (value.Length != 4) { throw new InvalidDataException("Invalid RAF dimension declaration."); }
                    uint first = BinaryPrimitives.ReadUInt16BigEndian(value.Span), second = BinaryPrimitives.ReadUInt16BigEndian(value.Span[2..]);
                    switch (tag)
                    {
                        case 256:
                            height = first; width = second;
                            properties.Add(new("raw", "sensor_width", width)); properties.Add(new("raw", "sensor_height", height));
                            break;
                        case 272:
                            properties.Add(new("raw", "crop_top", first)); properties.Add(new("raw", "crop_left", second));
                            break;
                        case 273:
                            properties.Add(new("raw", "crop_width", second)); properties.Add(new("raw", "crop_height", first));
                            break;
                        case 277:
                            properties.Add(new("raw", "aspect_ratio", second, first));
                            break;
                    }
                }
                else { properties.Add(new("raf", "tag_" + tag.ToString("X4"), MediaPropertyValue.FromBytes(value.Span))); }
            }
            if (width == 0 || height == 0) { throw new InvalidDataException("RAF sensor dimensions are missing or invalid."); }
            ulong orientation = 1;
            List<MediaMetadata> capture = [];
            if (previews.Images.Count != 0)
            {
                foreach (MediaMetadata exif in previews.Images[0].Info.Metadata.Children.Where(item => item.Scope == "Exif"))
                {
                    capture.Add(exif);
                    foreach (MediaProperty property in exif.Children.Where(item => item.Scope == "IFD0").SelectMany(item => item.Properties))
                    {
                        if (property.Name == "Orientation") { orientation = property.Values[0].AsUnsignedInteger(); }
                    }
                }
            }
            if (orientation is < 1 or > 8) { throw new InvalidDataException("Invalid RAF Exif orientation."); }
            properties.Add(new("raw", "orientation", orientation));
            return new(new("raf", orientation >= 5 ? height : width, orientation >= 5 ? width : height, ImageDimensionUnit.Pixels,
                metadata: ImageMetadataScopes.WithPreviews(properties, capture, previews.Images)), previews.Images) { ContainerExifOrientation = (int)orientation };
        }

        private static ReadOnlyMemory<byte> Slice(ReadOnlyMemory<byte> encoded, uint offset, uint length)
        {
            if (offset < 108 || offset > encoded.Length || length == 0 || length > encoded.Length - offset)
            { throw new InvalidDataException("RAF metadata or JPEG extent is invalid."); }
            return encoded.Slice((int)offset, (int)length);
        }
    }
}
