// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Containers.Mp4;
using Mediana.Core.Metadata;
using Mediana.Formats.IsoBmff;
using Mediana.Formats.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    /// <summary>Reads Canon CR3 capture directories, sensor geometry and original JPEG preview items and samples.</summary>
    public class Cr3MetadataReader : ImageMetadataReader
    {
        private static ReadOnlySpan<byte> CanonUuid => [0x85, 0xc0, 0xb6, 0x87, 0x82, 0x0f, 0x11, 0xe0, 0x81, 0x11, 0xf4, 0xce, 0x46, 0x2b, 0x6a, 0x48];
        private static ReadOnlySpan<byte> PreviewUuid => [0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88, 0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16];
        public override string ImplementationId => "cr3.managed.metadata";
        public override int ProbeLength => 24;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 16
            && prefix.Slice(4, 4).SequenceEqual("ftyp"u8) && (prefix.Slice(8, 4).SequenceEqual("crx "u8)
                || (prefix.Length >= 24 && BinaryPrimitives.ReadUInt32BigEndian(prefix) == 1 && prefix.Slice(16, 4).SequenceEqual("crx "u8)));

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span)) { throw new InvalidDataException("Canon RAW file brand is missing."); }
            IsoBmffBoxReader top = new(encoded);
            ReadOnlyMemory<byte> movie = default, canon = default;
            List<Mp4DataRange> media = [];
            RawJpegPreviews previews = new(cancellationToken);
            while (top.TryRead(out IsoBmffBox box))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (box.Type == IsoBmffBox.Code("moov"))
                {
                    if (!movie.IsEmpty) { throw new InvalidDataException("Duplicate Canon RAW movie."); }
                    movie = box.Payload;
                    IsoBmffBoxReader children = new(movie);
                    while (children.TryRead(out IsoBmffBox child))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (child.ExtendedType.Span.SequenceEqual(CanonUuid))
                        {
                            if (!canon.IsEmpty) { throw new InvalidDataException("Duplicate Canon RAW metadata directory."); }
                            canon = child.Payload;
                        }
                    }
                }
                else if (box.Type == IsoBmffBox.Code("mdat")) { media.Add(new(box.PayloadOffset, box.Payload.Length)); }
                else if (box.ExtendedType.Span.SequenceEqual(PreviewUuid)) { ReadPreviewContainer(box.Payload, previews, cancellationToken); }
            }
            if (canon.IsEmpty) { throw new InvalidDataException("Canon RAW metadata directory is missing."); }
            Dictionary<uint, ReadOnlyMemory<byte>> fields = [];
            IsoBmffBoxReader entries = new(canon);
            while (entries.TryRead(out IsoBmffBox box))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (box.Type == IsoBmffBox.Code("THMB")) { ReadJpegBox(box, previews); }
                else if (!fields.TryAdd(box.Type, box.Payload)) { throw new InvalidDataException("Duplicate Canon metadata box."); }
            }
            if (!fields.TryGetValue(IsoBmffBox.Code("CNCV"), out ReadOnlyMemory<byte> version) || !version.Span.StartsWith("CanonCR3"u8))
            { throw new NotSupportedException("The Canon RAW container is not a CR3 still-image presentation."); }
            List<MediaMetadata> capture = [];
            ulong orientation = 1, sensorWidth = 0, sensorHeight = 0;
            List<MediaProperty> properties = [new("raw", "family", "cr3"), new("canon", "compressor_version", Encoding.ASCII.GetString(version.Span).TrimEnd('\0'))];
            foreach ((string name, string scope) in new[] { ("CMT1", "IFD0"), ("CMT2", "Exif"), ("CMT3", "MakerNotes"), ("CMT4", "GPS") })
            {
                if (!fields.TryGetValue(IsoBmffBox.Code(name), out ReadOnlyMemory<byte> tiff)) { continue; }
                TiffDirectoryReader reader = new(tiff);
                TiffDirectory directory = reader.Read(reader.FirstOffset, cancellationToken);
                if (name == "CMT1")
                {
                    orientation = directory.Number(274, 1);
                    if (directory.Number(256) != 0) { properties.Add(new("canon", "declared_image_width", directory.Number(256))); }
                    if (directory.Number(257) != 0) { properties.Add(new("canon", "declared_image_height", directory.Number(257))); }
                }
                if (name == "CMT3" && directory.Fields.TryGetValue(224, out TiffField? sensor))
                {
                    if (sensor.Type is not (3 or 8) || sensor.Count < 9) { throw new InvalidDataException("Invalid Canon sensor information."); }
                    ushort Value(int index) => reader.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(sensor.Bytes.Span[(index * 2)..])
                        : BinaryPrimitives.ReadUInt16BigEndian(sensor.Bytes.Span[(index * 2)..]);
                    sensorWidth = Value(1); sensorHeight = Value(2);
                    properties.Add(new("raw", "sensor_left_border", (uint)Value(5))); properties.Add(new("raw", "sensor_top_border", (uint)Value(6)));
                    properties.Add(new("raw", "sensor_right_border", (uint)Value(7))); properties.Add(new("raw", "sensor_bottom_border", (uint)Value(8)));
                }
                capture.Add(new(name, children: [ExifMetadataReader.Read(reader, cancellationToken, rootScope: scope)]));
            }
            if (sensorWidth == 0 || sensorHeight == 0 || orientation is < 1 or > 8)
            { throw new InvalidDataException("Canon RAW sensor dimensions or orientation are missing or invalid."); }
            Cr3JpegTrackPreviews.Read(encoded, movie, media, previews, cancellationToken);
            properties.Add(new("raw", "sensor_width", sensorWidth)); properties.Add(new("raw", "sensor_height", sensorHeight));
            properties.Add(new("raw", "orientation", orientation));
            return new(new("cr3", orientation >= 5 ? sensorHeight : sensorWidth, orientation >= 5 ? sensorWidth : sensorHeight,
                ImageDimensionUnit.Pixels, metadata: ImageMetadataScopes.WithPreviews(properties, capture, previews.Images)), previews.Images);
        }

        private static void ReadPreviewContainer(ReadOnlyMemory<byte> payload, RawJpegPreviews previews, CancellationToken token)
        {
            ImageMetadataCursor cursor = new(payload);
            if (cursor.Dword() != 0) { throw new NotSupportedException("Unknown Canon preview container version."); }
            uint count = cursor.Dword();
            IsoBmffBoxReader reader = new(cursor.Take((uint)cursor.Remaining));
            uint read = 0;
            while (reader.TryRead(out IsoBmffBox box))
            {
                token.ThrowIfCancellationRequested();
                if (box.Type != IsoBmffBox.Code("PRVW")) { throw new InvalidDataException("Canon preview container requires PRVW items."); }
                ReadJpegBox(box, previews); read++;
            }
            if (count != read) { throw new InvalidDataException("Canon preview count disagrees with its entries."); }
        }

        private static void ReadJpegBox(IsoBmffBox box, RawJpegPreviews previews)
        {
            ImageMetadataCursor cursor = new(box.Payload);
            uint version = cursor.Dword();
            ushort width, height; uint length;
            if (box.Type == IsoBmffBox.Code("THMB"))
            {
                if (version is not (0 or 0x1000000)) { throw new NotSupportedException("Unknown Canon thumbnail version."); }
                width = cursor.Word(); height = cursor.Word(); length = cursor.Dword();
                if (version == 0) { cursor.Take(4); }
            }
            else
            {
                if (version != 0) { throw new NotSupportedException("Unknown Canon preview version."); }
                cursor.Take(2); width = cursor.Word(); height = cursor.Word(); cursor.Take(2); length = cursor.Dword();
            }
            if (width == 0 || height == 0 || length == 0) { throw new InvalidDataException("Invalid Canon JPEG preview declaration."); }
            previews.Add(cursor.Take(length), width, height);
        }
    }
}
