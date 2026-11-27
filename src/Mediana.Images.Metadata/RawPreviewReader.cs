// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.IO;

namespace Mediana.Images.Metadata
{
    public static class RawPreviewReader
    {
        private static readonly ImageMetadataReader[] Readers = [new TiffImageMetadataReader(), new RafMetadataReader(), new Cr3MetadataReader()];

        private static readonly int ProbeLength = Readers.Max(reader => reader.ProbeLength);

        /// <summary>Reads from the current position, leaving the stream open. Returns the smallest-area embedded JPEG
        /// whose longest side reaches the requested size, or the largest by area. Returns null for a recognized
        /// RAW without an embedded JPEG. Invalid directory or preview data propagates as a read error.</summary>
        public static async ValueTask<RawImagePreview?> ExtractAsync(Stream source, int requestedSize,
            int maximumInputBytes = int.MaxValue, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumInputBytes);
            cancellationToken.ThrowIfCancellationRequested();
            byte[] data;
            if (source.CanSeek)
            {
                long length = source.Length - source.Position;
                if (length < 0 || length > Math.Min(maximumInputBytes, Array.MaxLength))
                {
                    throw new InvalidDataException("The RAW stream length exceeds the input limit or is invalid.");
                }
                await using RandomAccessStreamByteSource indexedSource = new(source, leaveOpen: true);
                byte[] prefix = new byte[Math.Min((int)length, ProbeLength)];
                await indexedSource.ReadExactlyAtAsync(0, prefix, cancellationToken).ConfigureAwait(false);
                foreach (ImageMetadataReader reader in Readers)
                {
                    if (reader is IIndexedRawPreviewReader indexed && reader.CanRead(prefix, length))
                    {
                        RawImagePreview? result = await indexed.ExtractAsync(indexedSource, (int)length, requestedSize, cancellationToken).ConfigureAwait(false);
                        source.Seek(length, SeekOrigin.Current);
                        return result;
                    }
                }
                data = GC.AllocateUninitializedArray<byte>((int)length);
                await source.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                data = await BoundedStreamReader.ReadAsync(source, Math.Min(Array.MaxLength, maximumInputBytes), cancellationToken).ConfigureAwait(false);
            }
            foreach (ImageMetadataReader reader in Readers)
            {
                if (!reader.CanRead(data, data.Length)) { continue; }
                ImageInspection image = reader.InspectForPreview(data, cancellationToken);
                if (image.Info.FormatId == "tiff") { break; }
                return Select(image, requestedSize, cancellationToken);
            }
            throw new NotSupportedException("No camera RAW reader recognizes the input.");
        }

        private static RawImagePreview? Select(ImageInspection image, int requestedSize, CancellationToken token)
        {
            RawPreviewSelection selection = new(requestedSize);
            EncodedImagePreview? selected = null;
            foreach (EncodedImagePreview candidate in image.Previews)
            {
                token.ThrowIfCancellationRequested();
                if (selection.Consider(candidate.Info.Width, candidate.Info.Height)) { selected = candidate; }
            }
            if (selected is null) { return null; }
            int orientation = (selected as RawJpegCandidate)?.DirectoryOrientation ?? image.ContainerExifOrientation;
            token.ThrowIfCancellationRequested();
            return new(selected.Encoded.ToArray(), orientation, image.PreviewColorSpace, requestedSize);
        }
    }
}
