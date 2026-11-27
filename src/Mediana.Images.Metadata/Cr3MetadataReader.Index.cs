// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Formats.IsoBmff;
using Mediana.IO;

namespace Mediana.Images.Metadata
{
    public partial class Cr3MetadataReader
    {
        async ValueTask<RawImagePreview?> IIndexedRawPreviewReader.ExtractAsync(IRandomAccessByteSource source, int length,
            int requestedSize, CancellationToken token)
        {
            byte[] scratch = new byte[32];
            List<Mp4DataRange> media = [];
            Mp4DataRange? movie = null;
            List<Cr3PreviewExtent> declared = [];
            RawPreviewSelection selection = new(requestedSize);
            Cr3PreviewExtent? selected = null;
            void Consider(Cr3PreviewExtent candidate)
            {
                if (selection.Consider(candidate.Width, candidate.Height)) { selected = candidate; }
            }

            for (int offset = 0; offset < length;)
            {
                IsoBmffBoxHeader box = await ReadBoxAsync(source, offset, length - offset, scratch, token).ConfigureAwait(false);
                int start = offset + box.HeaderLength, end = offset + box.Length;
                if (box.Type == IsoBmffBox.Code("moov"))
                {
                    if (movie.HasValue) { throw new InvalidDataException("Duplicate Canon RAW movie."); }
                    movie = new(start, end - start);
                }
                else if (box.Type == IsoBmffBox.Code("mdat")) { media.Add(new(start, end - start)); }
                else if (box.ExtendedType.Span.SequenceEqual(PreviewUuid))
                {
                    await ReadPreviewContainerAsync(source, start, end, scratch, declared.Add, token).ConfigureAwait(false);
                }
                offset = end;
            }
            if (movie is not Mp4DataRange metadata) { throw new InvalidDataException("Canon RAW movie is missing."); }
            byte[] movieBytes = GC.AllocateUninitializedArray<byte>(checked((int)metadata.Length));
            await source.ReadExactlyAtAsync(metadata.Offset, movieBytes, token).ConfigureAwait(false);
            Cr3CameraMetadata camera = Cr3CameraMetadata.Read(movieBytes, (int)metadata.Offset, declared.Add, false, token);
            foreach (Cr3PreviewExtent entry in declared)
            {
                (int width, int height) = await RawJpegHeaderReader.ReadStoredDimensionsAsync(source, entry.Offset, entry.Length, token).ConfigureAwait(false);
                if (width != entry.Width || height != entry.Height) { throw new InvalidDataException("RAW preview dimensions disagree with its JPEG."); }
                Consider(entry);
            }
            foreach (Mp4DataRange sample in Cr3JpegTrackPreviews.Enumerate(movieBytes, media, token))
            {
                int size = checked((int)sample.Length);
                (int width, int height) = await RawJpegHeaderReader.ReadStoredDimensionsAsync(source, sample.Offset, size, token).ConfigureAwait(false);
                Consider(new(checked((int)sample.Offset), size, width, height));
            }
            if (selected is not Cr3PreviewExtent image) { return null; }

            byte[] jpeg = GC.AllocateUninitializedArray<byte>(image.Length);
            await source.ReadExactlyAtAsync(image.Offset, jpeg, token).ConfigureAwait(false);
            RawJpegPreviews.InspectJpeg(jpeg, image.Width, image.Height, token);
            return new(jpeg, camera.Orientation, camera.ColorSpace, requestedSize);
        }

        private static async ValueTask<IsoBmffBoxHeader> ReadBoxAsync(IRandomAccessByteSource source, int offset, int remaining,
            byte[] scratch, CancellationToken token)
        {
            if (remaining < 8) { throw new InvalidDataException("Truncated ISO BMFF box header."); }
            await source.ReadExactlyAtAsync(offset, scratch.AsMemory(0, 8), token).ConfigureAwait(false);
            int required = IsoBmffBoxHeader.RequiredBytes(scratch);
            if (required > remaining) { throw new InvalidDataException("Truncated ISO BMFF box header."); }
            await source.ReadExactlyAtAsync(offset + 8, scratch.AsMemory(8, required - 8), token).ConfigureAwait(false);
            return IsoBmffBoxHeader.Read(scratch.AsMemory(0, required), remaining);
        }

        private static async ValueTask ReadPreviewContainerAsync(IRandomAccessByteSource source, int start, int end,
            byte[] scratch, Action<Cr3PreviewExtent> consider, CancellationToken token)
        {
            if (end - start < 8) { throw new InvalidDataException("Truncated Canon preview container."); }
            await source.ReadExactlyAtAsync(start, scratch.AsMemory(0, 8), token).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32BigEndian(scratch) != 0) { throw new NotSupportedException("Unknown Canon preview container version."); }
            uint count = BinaryPrimitives.ReadUInt32BigEndian(scratch.AsSpan(4));
            uint read = 0;
            for (int offset = start + 8; offset < end; read++)
            {
                IsoBmffBoxHeader box = await ReadBoxAsync(source, offset, end - offset, scratch, token).ConfigureAwait(false);
                if (box.Type != IsoBmffBox.Code("PRVW")) { throw new InvalidDataException("Canon preview container requires PRVW items."); }
                const int declarationBytes = 16;
                int payloadLength = box.Length - box.HeaderLength, payloadOffset = offset + box.HeaderLength;
                if (payloadLength < declarationBytes) { throw new InvalidDataException("Truncated Canon JPEG preview declaration."); }
                await source.ReadExactlyAtAsync(payloadOffset, scratch.AsMemory(0, declarationBytes), token).ConfigureAwait(false);
                consider(Cr3PreviewExtent.FromBox(box.Type, payloadOffset, payloadLength, scratch.AsMemory(0, declarationBytes)));
                offset += box.Length;
            }
            if (count != read) { throw new InvalidDataException("Canon preview count disagrees with its entries."); }
        }
    }
}
