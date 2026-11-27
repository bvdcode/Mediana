// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Metadata;
using Mediana.Formats.IsoBmff;
using System.Buffers.Binary;

namespace Mediana.Images.Metadata
{
    /// <summary>Reads Canon CR3 capture directories, sensor geometry and original JPEG preview items and samples.</summary>
    public partial class Cr3MetadataReader : ImageMetadataReader, IIndexedRawPreviewReader
    {
        private static ReadOnlySpan<byte> PreviewUuid => [0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88, 0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16];
        public override string ImplementationId => "cr3.managed.metadata";
        public override int ProbeLength => 24;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 16
            && prefix.Slice(4, 4).SequenceEqual("ftyp"u8) && (prefix.Slice(8, 4).SequenceEqual("crx "u8)
                || (prefix.Length >= 24 && BinaryPrimitives.ReadUInt32BigEndian(prefix) == 1 && prefix.Slice(16, 4).SequenceEqual("crx "u8)));

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            Inspect(encoded, true, cancellationToken);

        internal override ImageInspection InspectForPreview(ReadOnlyMemory<byte> encoded, CancellationToken token) =>
            Inspect(encoded, false, token);

        private ImageInspection Inspect(ReadOnlyMemory<byte> encoded, bool captureMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span)) { throw new InvalidDataException("Canon RAW file brand is missing."); }
            IsoBmffBoxReader top = new(encoded);
            IsoBmffBox movie = default;
            List<Mp4DataRange> media = [];
            RawJpegPreviews previews = new(cancellationToken);
            void Add(Cr3PreviewExtent extent) => previews.Add(encoded.Slice(extent.Offset, extent.Length), extent.Width, extent.Height);
            while (top.TryRead(out IsoBmffBox box))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (box.Type == IsoBmffBox.Code("moov"))
                {
                    if (!movie.Payload.IsEmpty) { throw new InvalidDataException("Duplicate Canon RAW movie."); }
                    movie = box;
                }
                else if (box.Type == IsoBmffBox.Code("mdat")) { media.Add(new(box.PayloadOffset, box.Payload.Length)); }
                else if (box.ExtendedType.Span.SequenceEqual(PreviewUuid)) { ReadPreviewContainer(box.Payload, box.PayloadOffset, Add, cancellationToken); }
            }
            Cr3CameraMetadata camera = Cr3CameraMetadata.Read(movie.Payload, movie.PayloadOffset, Add, captureMetadata, cancellationToken);
            foreach (Mp4DataRange sample in Cr3JpegTrackPreviews.Enumerate(movie.Payload, media, cancellationToken))
            {
                previews.Add(encoded.Slice(checked((int)sample.Offset), checked((int)sample.Length)));
            }
            bool rotate = camera.Orientation >= 5;
            return new(new("cr3", rotate ? camera.Height : camera.Width, rotate ? camera.Width : camera.Height,
                ImageDimensionUnit.Pixels, metadata: ImageMetadataScopes.WithPreviews(camera.Properties, camera.Capture, previews.Images)), previews.Images)
            { ContainerExifOrientation = camera.Orientation, PreviewColorSpace = camera.ColorSpace };
        }

        private static void ReadPreviewContainer(ReadOnlyMemory<byte> payload, int payloadOffset, Action<Cr3PreviewExtent> add, CancellationToken token)
        {
            ImageMetadataCursor cursor = new(payload);
            if (cursor.Dword() != 0) { throw new NotSupportedException("Unknown Canon preview container version."); }
            uint count = cursor.Dword();
            IsoBmffBoxReader reader = new(cursor.Take((uint)cursor.Remaining), payloadOffset + 8);
            uint read = 0;
            while (reader.TryRead(out IsoBmffBox box))
            {
                token.ThrowIfCancellationRequested();
                if (box.Type != IsoBmffBox.Code("PRVW")) { throw new InvalidDataException("Canon preview container requires PRVW items."); }
                add(Cr3PreviewExtent.FromBox(box.Type, box.PayloadOffset, box.Payload.Length, box.Payload)); read++;
            }
            if (count != read) { throw new InvalidDataException("Canon preview count disagrees with its entries."); }
        }
    }
}
