// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Formats.Metadata
{
    /// <summary>Inspects TIFF and TIFF-based DNG, CR2, NEF and ARW without decoding image samples.
    /// Camera MakerNotes remain opaque. Raster dimensions refer to the selected sensor directory;
    /// crop, orientation, CFA and capture properties retain their declared values in Exif metadata.</summary>
    public static class TiffMetadataReader
    {
        public static ImageInfo Read(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TiffDirectoryReader reader = new(encoded);
            TiffDirectory first = reader.Read(reader.FirstOffset, cancellationToken);
            MediaMetadata exif = ExifMetadataReader.Read(reader, cancellationToken);
            string format = Identify(encoded.Span, first, reader.Directories);
            TiffDirectory selected = first;
            if (format != "tiff")
            {
                ulong largest = 0;
                foreach (TiffDirectory directory in reader.Directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ulong photo = directory.Number(262), width = directory.Number(256), height = directory.Number(257);
                    if ((directory.Number(254) & 1) != 0 || photo is not (32803 or 34892) || width == 0 || height == 0) { continue; }
                    ulong area = checked(width * height);
                    if (area > largest) { largest = area; selected = directory; }
                }
                if (format == "cr2")
                {
                    uint rawOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded[12..].Span);
                    if (!reader.LittleEndian) { rawOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(encoded[12..].Span); }
                    TiffDirectory sensor = reader.Read(rawOffset, cancellationToken);
                    if (sensor.Number(256) > 0 && sensor.Number(257) > 0) { selected = sensor; }
                }
            }
            ulong sourceWidth = selected.Number(256), sourceHeight = selected.Number(257), orientation = selected.Number(274, first.Number(274, 1));
            if (sourceWidth == 0 || sourceHeight == 0 || orientation is < 1 or > 8)
            { throw new InvalidDataException("Invalid TIFF image dimensions or orientation."); }
            int? pages = null;
            if (format == "tiff")
            {
                HashSet<ulong> seen = []; ulong offset = reader.FirstOffset;
                while (offset != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!seen.Add(offset)) { throw new InvalidDataException("Cyclic TIFF page chain."); }
                    offset = reader.Read(offset, cancellationToken).NextOffset;
                }
                pages = seen.Count;
            }
            List<MediaProperty> properties = [new("tiff", "big_tiff", reader.BigTiff), new("tiff", "little_endian", reader.LittleEndian),
                new("tiff", "source_width", sourceWidth), new("tiff", "source_height", sourceHeight), new("tiff", "orientation", orientation)];
            if (format != "tiff")
            {
                properties.Add(new("raw", "family", format));
                properties.Add(new("raw", "photometric_interpretation", selected.Number(262)));
                properties.Add(new("raw", "compression", selected.Number(259, 1)));
            }
            return new(format, orientation >= 5 ? sourceHeight : sourceWidth, orientation >= 5 ? sourceWidth : sourceHeight,
                ImageDimensionUnit.Pixels, pages, metadata: new("image", properties, [exif]));
        }
        private static string Identify(ReadOnlySpan<byte> encoded, TiffDirectory first, IReadOnlyCollection<TiffDirectory> directories)
        {
            if (directories.Any(directory => directory.Fields.ContainsKey(50706))) { return "dng"; }
            if (encoded.Length >= 16 && encoded.Slice(8, 4).SequenceEqual("CR\x02\0"u8)) { return "cr2"; }
            if (!directories.Any(directory => directory.Number(262) == 32803)) { return "tiff"; }
            string make = first.Fields.TryGetValue(271, out TiffField? field) && field.Type == 2
                ? Encoding.ASCII.GetString(field.Bytes.Span).TrimEnd('\0').Trim() : string.Empty;
            if (make.Equals("NIKON CORPORATION", StringComparison.OrdinalIgnoreCase) || make.Equals("NIKON", StringComparison.OrdinalIgnoreCase)) { return "nef"; }
            if (make.Equals("SONY", StringComparison.OrdinalIgnoreCase)) { return "arw"; }
            return "tiff";
        }
    }
}
