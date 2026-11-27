// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    internal class RawJpegPreviews(CancellationToken token)
    {
        private static readonly JpegImageDecoder Jpeg = CreateDecoder(1);
        private readonly List<EncodedImagePreview> images = [];
        private readonly HashSet<ReadOnlyMemory<byte>> slices = [];
        internal IReadOnlyList<EncodedImagePreview> Images => images;

        internal static JpegImageDecoder CreateDecoder(int orientation, JpegColorSpace? colorSpace = null) => new(CreateOptions(orientation, colorSpace));

        internal static JpegDecodeOptions CreateOptions(int orientation, JpegColorSpace? colorSpace = null) => new()
        {
            ContainerColorSpace = colorSpace,
            ContainerExifOrientation = orientation,
            MaximumEncodedBytes = Array.MaxLength,
            MaximumPixels = Array.MaxLength / 4
        };

        internal void Add(ReadOnlyMemory<byte> jpeg, int? width = null, int? height = null, int? directoryOrientation = null, bool embeddedField = false)
        {
            token.ThrowIfCancellationRequested();
            if (slices.Contains(jpeg)) { return; }
            int length = jpeg.Length;
            Core.Metadata.ImageInfo info = embeddedField ? Jpeg.ReadEmbeddedInfo(jpeg, out length, token) : Jpeg.ReadInfo(jpeg, token);
            jpeg = jpeg[..length];
            if (!slices.Add(jpeg)) { return; }
            ValidateDimensions(info, width, height);
            images.Add(new RawJpegCandidate(info, jpeg, directoryOrientation));
        }

        internal static void InspectJpeg(ReadOnlyMemory<byte> jpeg, int width, int height, CancellationToken token) =>
            ValidateDimensions(Jpeg.ReadInfo(jpeg, token), width, height);

        private static void ValidateDimensions(Core.Metadata.ImageInfo info, int? width, int? height)
        {
            Core.Metadata.MediaProperty dimensions = info.Metadata.Properties.Single(item => item.Name == "stored_dimensions");
            if ((width.HasValue && dimensions.Values[0].AsInteger() != width) || (height.HasValue && dimensions.Values[1].AsInteger() != height))
            { throw new InvalidDataException("RAW preview dimensions disagree with its JPEG."); }
        }

        internal void ReadPair(TiffDirectoryReader reader, TiffDirectory directory, ushort offsetTag, ushort lengthTag, bool imageDirectory = false)
        {
            bool hasOffset = directory.Fields.ContainsKey(offsetTag), hasLength = directory.Fields.ContainsKey(lengthTag);
            if (!hasOffset && !hasLength) { return; }
            if (!hasOffset || !hasLength) { throw new InvalidDataException("RAW JPEG preview offset and length must occur together."); }
            ulong offset = directory.Number(offsetTag), length = directory.Number(lengthTag);
            if (offset == 0 && length == 0) { return; }
            if (offset < (ulong)reader.HeaderLength || length == 0) { throw new InvalidDataException("Invalid RAW JPEG preview extent."); }
            Add(reader.Slice(offset, length), directoryOrientation: imageDirectory ? DirectoryOrientation(directory) : null);
        }

        internal static int? DirectoryOrientation(TiffDirectory directory)
        {
            if (!directory.Fields.TryGetValue(274, out TiffField? field)) { return null; }
            ulong orientation = field.Unsigned();
            if (orientation is < 1 or > 8) { throw new InvalidDataException("Invalid RAW preview orientation."); }
            return (int)orientation;
        }

        internal void ReadDirectory(TiffDirectoryReader reader, TiffDirectory directory, bool includeJpegStrips = true)
        {
            if (directory.Number(262) is 32803 or 34892) { return; }
            ReadPair(reader, directory, 513, 514, imageDirectory: true);
            if (includeJpegStrips && !directory.Fields.ContainsKey(347) && directory.Number(259) is 6 or 7 && directory.Fields.ContainsKey(273) && directory.Fields.ContainsKey(279)
                && directory.Fields[273].Count == 1 && directory.Fields[279].Count == 1)
            { ReadPair(reader, directory, 273, 279, imageDirectory: true); }
        }
    }
}
