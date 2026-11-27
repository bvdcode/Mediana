// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    /// <summary>Inspects TIFF and TIFF-based camera RAW directories and returns original JPEG previews.</summary>
    public class TiffImageMetadataReader : ImageMetadataReader
    {
        public override string ImplementationId => "tiff.managed.metadata";
        public override int ProbeLength => 4;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 4
            && ((prefix.StartsWith("II"u8) && IsIdentifier(BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..])))
                || (prefix.StartsWith("MM"u8) && IsIdentifier(BinaryPrimitives.ReadUInt16BigEndian(prefix[2..]))));

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            InspectTiff(encoded, false, cancellationToken);

        internal override ImageInspection InspectForPreview(ReadOnlyMemory<byte> encoded, CancellationToken token) =>
            InspectTiff(encoded, true, token);

        private ImageInspection InspectTiff(ReadOnlyMemory<byte> encoded, bool previewOnly, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span)) { throw new InvalidDataException("Invalid TIFF or camera RAW signature."); }
            ushort version = encoded.Span[0] == 73 ? BinaryPrimitives.ReadUInt16LittleEndian(encoded.Span[2..])
                : BinaryPrimitives.ReadUInt16BigEndian(encoded.Span[2..]);
            TiffHeaderKind kind = version switch
            {
                42 or 43 => TiffHeaderKind.Standard,
                0x4f52 or 0x5352 => TiffHeaderKind.Olympus,
                0x55 => TiffHeaderKind.Panasonic,
                _ => throw new InvalidDataException("Invalid TIFF identifier.")
            };
            TiffMetadataDocument document = TiffMetadataReader.ReadDocument(encoded, kind, cancellationToken, previewOnly);
            RawJpegPreviews previews = new(cancellationToken, tiffCandidates: document.Info.FormatId != "tiff", previewOnly: previewOnly);
            foreach (TiffDirectory directory in document.Reader.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (directory == document.Cr2Sensor) { continue; }
                previews.ReadDirectory(document.Reader, directory, includeJpegStrips: document.Info.FormatId != "tiff");
                if (kind == TiffHeaderKind.Panasonic)
                {
                    foreach (ushort tag in new ushort[] { 46, 295 })
                    {
                        if (directory.Fields.TryGetValue(tag, out TiffField? field))
                        { previews.Collect(() => previews.Add(field.Bytes, directoryOrientation: RawJpegPreviews.DirectoryOrientation(directory), embeddedField: true)); }
                    }
                }
                if (directory.Fields.TryGetValue(37500, out TiffField? note))
                { previews.Collect(() => RawMakerNotePreviews.Read(encoded, document.Reader.LittleEndian, note, document.Info.FormatId, previews, cancellationToken)); }
                if (document.Info.FormatId == "dng" && directory.Fields.TryGetValue(50740, out TiffField? privateData))
                {
                    previews.Collect(() =>
                    {
                        if (privateData.Bytes.Span.StartsWith("PENTAX \0"u8))
                        { RawMakerNotePreviews.Read(encoded, document.Reader.LittleEndian, privateData, "dng", previews, cancellationToken); }
                    });
                }
            }
            ImageInfo source = document.Info;
            MediaMetadata metadata = ImageMetadataScopes.WithPreviews(source.Metadata.Properties, source.Metadata.Children, previews.Images);
            return new(new(source.FormatId, source.Width, source.Height, source.DimensionUnit, source.PageCount,
                source.Duration, source.FrameRate, source.IsPreviewCanvas, metadata), previews.Images)
            { ContainerExifOrientation = document.Orientation };
        }

        private static bool IsIdentifier(ushort identifier) => identifier is 42 or 43 or 0x4f52 or 0x5352 or 0x55;
    }
}
