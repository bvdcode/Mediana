// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    internal class RawJpegPreviews(CancellationToken token)
    {
        private static readonly JpegImageDecoder Jpeg = new();
        private readonly List<EncodedImagePreview> images = [];
        private readonly HashSet<ReadOnlyMemory<byte>> slices = [];
        internal IReadOnlyList<EncodedImagePreview> Images => images;

        internal void Add(ReadOnlyMemory<byte> jpeg, int? width = null, int? height = null)
        {
            token.ThrowIfCancellationRequested();
            if (!slices.Add(jpeg)) { return; }
            Core.Metadata.ImageInfo info = Jpeg.ReadInfo(jpeg, token);
            Core.Metadata.MediaProperty dimensions = info.Metadata.Properties.Single(item => item.Name == "stored_dimensions");
            if ((width.HasValue && dimensions.Values[0].AsInteger() != width) || (height.HasValue && dimensions.Values[1].AsInteger() != height))
            { throw new InvalidDataException("RAW preview dimensions disagree with its JPEG."); }
            images.Add(new(info, jpeg));
        }

        internal void ReadPair(TiffDirectoryReader reader, TiffDirectory directory, ushort offsetTag, ushort lengthTag)
        {
            bool hasOffset = directory.Fields.ContainsKey(offsetTag), hasLength = directory.Fields.ContainsKey(lengthTag);
            if (!hasOffset && !hasLength) { return; }
            if (!hasOffset || !hasLength) { throw new InvalidDataException("RAW JPEG preview offset and length must occur together."); }
            ulong offset = directory.Number(offsetTag), length = directory.Number(lengthTag);
            if (offset == 0 && length == 0) { return; }
            if (offset < (ulong)reader.HeaderLength || length == 0) { throw new InvalidDataException("Invalid RAW JPEG preview extent."); }
            Add(reader.Slice(offset, length));
        }

        internal void ReadDirectory(TiffDirectoryReader reader, TiffDirectory directory, bool includeJpegStrips = true)
        {
            if (directory.Number(262) is 32803 or 34892) { return; }
            ReadPair(reader, directory, 513, 514);
            if (includeJpegStrips && !directory.Fields.ContainsKey(347) && directory.Number(259) is 6 or 7 && directory.Fields.ContainsKey(273) && directory.Fields.ContainsKey(279)
                && directory.Fields[273].Count == 1 && directory.Fields[279].Count == 1)
            { ReadPair(reader, directory, 273, 279); }
        }
    }
}
