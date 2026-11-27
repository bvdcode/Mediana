// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Metadata;
using Mediana.Formats.IsoBmff;
using Mediana.Formats.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    internal class Cr3CameraMetadata
    {
        private static ReadOnlySpan<byte> CanonUuid => [0x85, 0xc0, 0xb6, 0x87, 0x82, 0x0f, 0x11, 0xe0, 0x81, 0x11, 0xf4, 0xce, 0x46, 0x2b, 0x6a, 0x48];
        internal required ulong Width { get; init; }
        internal required ulong Height { get; init; }
        internal required int Orientation { get; init; }
        internal required JpegColorSpace? ColorSpace { get; init; }
        internal required List<MediaProperty> Properties { get; init; }
        internal required List<MediaMetadata> Capture { get; init; }

        internal static Cr3CameraMetadata Read(ReadOnlyMemory<byte> movie, int movieOffset, Action<Cr3PreviewExtent> addThumbnail,
            bool captureMetadata, CancellationToken cancellationToken)
        {
            IsoBmffBoxReader children = new(movie, movieOffset);
            IsoBmffBox canon = default;
            while (children.TryRead(out IsoBmffBox child))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!child.ExtendedType.Span.SequenceEqual(CanonUuid)) { continue; }
                if (!canon.Payload.IsEmpty) { throw new InvalidDataException("Duplicate Canon RAW metadata directory."); }
                canon = child;
            }
            if (canon.Payload.IsEmpty) { throw new InvalidDataException("Canon RAW metadata directory is missing."); }
            Dictionary<uint, ReadOnlyMemory<byte>> fields = [];
            IsoBmffBoxReader entries = new(canon.Payload, canon.PayloadOffset);
            while (entries.TryRead(out IsoBmffBox box))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (box.Type == IsoBmffBox.Code("THMB")) { addThumbnail(Cr3PreviewExtent.FromBox(box.Type, box.PayloadOffset, box.Payload.Length, box.Payload)); }
                else if (!fields.TryAdd(box.Type, box.Payload)) { throw new InvalidDataException("Duplicate Canon metadata box."); }
            }
            if (!fields.TryGetValue(IsoBmffBox.Code("CNCV"), out ReadOnlyMemory<byte> version) || !version.Span.StartsWith("CanonCR3"u8))
            { throw new NotSupportedException("The Canon RAW container is not a CR3 still-image presentation."); }
            JpegColorSpace? colorSpace = null;
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
                if (name == "CMT2" && directory.Fields.TryGetValue(0x9101, out TiffField? configuration))
                {
                    colorSpace = configuration.Bytes.Span switch
                    {
                        [1, 2, 3, 0] => JpegColorSpace.YCbCr,
                        [4, 5, 6, 0] => JpegColorSpace.Rgb,
                        _ => null
                    };
                }
                if (captureMetadata) { capture.Add(new(name, children: [ExifMetadataReader.Read(reader, cancellationToken, rootScope: scope)])); }
            }
            if (sensorWidth == 0 || sensorHeight == 0 || orientation is < 1 or > 8)
            { throw new InvalidDataException("Canon RAW sensor dimensions or orientation are missing or invalid."); }
            properties.Add(new("raw", "sensor_width", sensorWidth)); properties.Add(new("raw", "sensor_height", sensorHeight));
            properties.Add(new("raw", "orientation", orientation));
            return new()
            {
                Width = sensorWidth,
                Height = sensorHeight,
                Orientation = (int)orientation,
                ColorSpace = colorSpace,
                Properties = properties,
                Capture = capture
            };
        }
    }
}
