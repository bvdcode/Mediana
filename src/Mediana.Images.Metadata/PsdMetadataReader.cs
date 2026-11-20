// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;

namespace Mediana.Images.Metadata
{
    public class PsdMetadataReader : ImageMetadataReader
    {
        private static readonly JpegImageDecoder Jpeg = new();
        public override string ImplementationId => "psd.managed.metadata";
        public override int ProbeLength => 6;
        public override bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("8BPS"u8)
            && prefix.Length >= 6 && BinaryPrimitives.ReadUInt16BigEndian(prefix[4..]) is 1 or 2;

        public override ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span)) { throw new InvalidDataException("Invalid PSD signature or version."); }
            ImageMetadataCursor cursor = new(encoded);
            cursor.Take(4); int version = cursor.Word();
            if (cursor.Take(6).Span.ContainsAnyExcept((byte)0)) { throw new InvalidDataException("PSD reserved header bytes must be zero."); }
            int channels = cursor.Word(); uint height = cursor.Dword(), width = cursor.Dword();
            int depth = cursor.Word(), mode = cursor.Word(), maximum = version == 1 ? 30000 : 300000;
            if (channels is < 1 or > 56 || width == 0 || height == 0 || width > maximum || height > maximum
                || depth is not (1 or 8 or 16 or 32) || mode is not (0 or 1 or 2 or 3 or 4 or 7 or 8 or 9))
            { throw new InvalidDataException("Invalid PSD dimensions, depth, channel count or color mode."); }
            cursor.Take(cursor.Dword());
            ImageMetadataCursor resources = new(cursor.Take(cursor.Dword()));
            List<MediaProperty> properties = [new("psd", "version", version), new("psd", "channels", channels),
                new("psd", "bit_depth", depth), new("psd", "color_mode", mode),
                new("image", "source_width", width), new("image", "source_height", height)];
            List<MediaMetadata> children = [];
            List<EncodedImagePreview> previews = [];
            while (resources.Remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!resources.Take(4).Span.SequenceEqual("8BIM"u8)) { throw new InvalidDataException("Invalid PSD image resource signature."); }
                int id = resources.Word(), nameLength = resources.Byte();
                ReadOnlyMemory<byte> name = resources.Take((uint)nameLength);
                if ((nameLength & 1) == 0) { resources.Take(1); }
                uint length = resources.Dword(); ReadOnlyMemory<byte> payload = resources.Take(length);
                if ((length & 1) != 0) { resources.Take(1); }
                List<MediaProperty> resource = [new("psd", "id", id), new("psd", "byte_length", length)];
                if (nameLength != 0) { resource.Add(new("psd", "pascal_name", MediaPropertyValue.FromBytes(name.Span))); }
                List<MediaMetadata> nested = [];
                switch (id)
                {
                    case 1058: case 1059:
                        nested.Add(ExifMetadataReader.Read(payload, cancellationToken));
                        break;
                    case 1039:
                        resource.Add(new("icc", "profile", MediaPropertyValue.FromBytes(payload.Span)));
                        break;
                    case 1060:
                        resource.Add(new("xmp", "packet", Encoding.UTF8.GetString(payload.Span)));
                        break;
                    case 1033: case 1036:
                        ReadThumbnail(payload, id, resource, previews, cancellationToken);
                        break;
                }
                children.Add(new("resource/" + children.Count, resource, nested));
            }
            ulong layerBytes = version == 1 ? cursor.Dword() : BinaryPrimitives.ReadUInt64BigEndian(cursor.Take(8).Span);
            cursor.Take(layerBytes);
            int compression = cursor.Word();
            if (compression > 3) { throw new InvalidDataException("Invalid PSD image compression method."); }
            properties.Add(new("psd", "compression", compression));
            return new(new(version == 1 ? "psd" : "psb", width, height, ImageDimensionUnit.Pixels,
                metadata: ImageMetadataScopes.WithPreviews(properties, children, previews)), previews);
        }

        private static void ReadThumbnail(ReadOnlyMemory<byte> payload, int id, List<MediaProperty> metadata,
            List<EncodedImagePreview> previews, CancellationToken token)
        {
            ImageMetadataCursor cursor = new(payload);
            uint format = cursor.Dword(), width = cursor.Dword(), height = cursor.Dword(), stride = cursor.Dword();
            uint size = cursor.Dword(), compressed = cursor.Dword(); int bits = cursor.Word(), planes = cursor.Word();
            if (width == 0 || height == 0 || bits != 24 || planes != 1 || format > 1
                || stride != ((ulong)width * 24 + 31) / 32 * 4 || size != (ulong)stride * height || compressed != cursor.Remaining)
            { throw new InvalidDataException("Invalid PSD thumbnail declaration."); }
            metadata.Add(new("psd", "thumbnail_format", format));
            metadata.Add(new("psd", "thumbnail_width", width)); metadata.Add(new("psd", "thumbnail_height", height));
            metadata.Add(new("psd", "thumbnail_component_order", id == 1033 ? "BGR" : "RGB"));
            if (format == 0) { return; }
            ReadOnlyMemory<byte> jpeg = cursor.Take(compressed);
            ImageInfo info = Jpeg.ReadInfo(jpeg, token);
            if (info.Width != width || info.Height != height) { throw new InvalidDataException("PSD thumbnail dimensions disagree with its JPEG."); }
            previews.Add(new(new("jpeg", width, height, ImageDimensionUnit.Pixels,
                metadata: new("preview", [new("psd", "component_order", id == 1033 ? "BGR" : "RGB")], [info.Metadata])), jpeg));
        }
    }
}
