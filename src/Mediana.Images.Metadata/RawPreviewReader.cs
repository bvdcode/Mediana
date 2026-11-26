// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.IO;

namespace Mediana.Images.Metadata
{
    public static class RawPreviewReader
    {
        private static readonly ImageMetadataReader[] Readers = [new TiffImageMetadataReader(), new RafMetadataReader(), new Cr3MetadataReader()];

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
            EncodedImagePreview? sufficient = null, largest = null;
            double smallestArea = double.PositiveInfinity, largestArea = 0;
            foreach (EncodedImagePreview candidate in image.Previews)
            {
                token.ThrowIfCancellationRequested();
                double width = candidate.Info.Width, height = candidate.Info.Height;
                double area = width * height;
                if (area > largestArea) { largest = candidate; largestArea = area; }
                if (Math.Max(width, height) >= requestedSize && area < smallestArea)
                {
                    sufficient = candidate;
                    smallestArea = area;
                }
            }
            EncodedImagePreview? selected = sufficient ?? largest;
            if (selected is null) { return null; }
            int orientation = (selected as RawJpegCandidate)?.DirectoryOrientation ?? image.ContainerExifOrientation;
            token.ThrowIfCancellationRequested();
            return new(selected.Encoded, orientation, image.PreviewColorSpace);
        }
    }
}
