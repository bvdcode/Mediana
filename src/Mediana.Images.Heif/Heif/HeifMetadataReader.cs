// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Images.Heif.Container
{
    internal static class HeifMetadataReader
    {
        internal static MediaMetadata Read(HeifContainer container, ImageProperties primary, CancellationToken token)
        {
            HeifItem item = container.Items[container.PrimaryItemId];
            List<MediaProperty> properties = [.. container.FileProperties,
                new("heif", "primary_item_id", item.Id), new("heif", "primary_item_type", TypeName(item.Type)),
                new("heif", "item_count", container.Items.Count), new("heif", "stored_dimensions", primary.Width, primary.Height)];
            List<MediaMetadata> children = [];
            AddProperties(primary, properties);
            AddTransforms(primary, children);
            Dictionary<IReadOnlyList<PropertyAssociation>, List<HeifItem>> coded = new(new PropertyAssociationComparer());
            foreach (HeifItem entry in container.Items.Values)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Type == HeifBox.Code("hvc1"))
                {
                    if (!coded.TryGetValue(entry.Associations, out List<HeifItem>? group))
                    {
                        group = [];
                        coded.Add(entry.Associations, group);
                    }
                    group.Add(entry);
                }
                else if (entry.Type == HeifBox.Code("Exif"))
                {
                    ReadOnlyMemory<byte> data = container.ReadData(entry.Id);
                    if (data.Length < 4) { throw new InvalidDataException("Truncated HEIF Exif offset."); }
                    uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.Span);
                    if (offset > data.Length - 4) { throw new InvalidDataException("HEIF Exif offset exceeds its item."); }
                    children.Add(new("item/" + entry.Id.ToString(CultureInfo.InvariantCulture),
                        [new("heif", "item_type", "Exif")], [ExifMetadataReader.Read(data[(4 + (int)offset)..], token)]));
                }
            }
            int encodingIndex = 0;
            foreach (List<HeifItem> group in coded.Values)
            {
                token.ThrowIfCancellationRequested();
                ImageProperties source = ImageProperties.Read(container, group[0]);
                DecoderConfiguration configuration = source.Configuration ?? throw new InvalidDataException("HEVC image configuration is missing.");
                List<MediaProperty> encoding = [new("media", "codec", "hevc"), new("media", "source_dimensions", source.Width, source.Height),
                    new("heif", "item_count", group.Count), new("hevc", "profile_space", configuration.ProfileSpace),
                    new("hevc", "profile_idc", configuration.Profile), new("hevc", "level_idc", configuration.Level),
                    new("hevc", "high_tier", configuration.HighTier), new("hevc", "chroma_format_idc", configuration.ChromaFormat),
                    new("hevc", "luma_bit_depth", configuration.LumaBitDepth)];
                if (configuration.ChromaFormat != 0) { encoding.Add(new("hevc", "chroma_bit_depth", configuration.ChromaBitDepth)); }
                AddProperties(source, encoding);
                children.Add(new("encoding/" + encodingIndex.ToString(CultureInfo.InvariantCulture), encoding));
                encodingIndex++;
            }
            if (item.Type == HeifBox.Code("grid"))
            {
                GridImageDecoder.ReadInputs(container, item, primary, out int rows, out int columns);
                properties.Add(new("heif", "grid_rows", rows));
                properties.Add(new("heif", "grid_columns", columns));
            }
            foreach (IGrouping<uint, HeifItem> group in container.Items.Values.GroupBy(entry => entry.Type))
            {
                properties.Add(new("heif", "item_type/" + TypeName(group.Key), group.Count()));
            }
            return new("image", properties, children);
        }

        private static void AddProperties(ImageProperties image, List<MediaProperty> properties)
        {
            if (!image.PixelDepths.IsEmpty)
            {
                properties.Add(new("heif", "component_bit_depths", image.PixelDepths.ToArray().Select(value => (MediaPropertyValue)(uint)value).ToArray()));
            }
            if (image.Color is ColorEncoding color)
            {
                properties.AddRange([new("cicp", "primaries", color.Primaries), new("cicp", "transfer", color.Transfer),
                    new("cicp", "matrix", color.Matrix), new("cicp", "full_range", color.FullRange)]);
            }
            if (!image.IccProfile.IsEmpty)
            {
                properties.Add(new("icc", "profile", MediaPropertyValue.FromBytes(image.IccProfile.Span)));
                properties.Add(new("icc", "restricted", image.IccRestricted));
            }
            if (image.AuxiliaryType is string auxiliary)
            {
                properties.Add(new("heif", "auxiliary_type", auxiliary));
            }
        }

        private static void AddTransforms(ImageProperties image, List<MediaMetadata> children)
        {
            foreach (HeifBox transform in image.Transforms)
            {
                List<MediaProperty> properties = [new("heif", "type", TypeName(transform.Type))];
                if (transform.Type == HeifBox.Code("irot"))
                {
                    properties.Add(new("heif", "counterclockwise_degrees", transform.Payload.Span[0] * 90));
                }
                else if (transform.Type == HeifBox.Code("imir"))
                {
                    properties.Add(new("heif", "mirror_axis", (uint)transform.Payload.Span[0]));
                }
                else if (transform.Type == HeifBox.Code("clap"))
                {
                    ReadOnlySpan<byte> data = transform.Payload.Span;
                    properties.Add(new("heif", "clean_aperture_dimensions", Fraction(data[..8], false), Fraction(data.Slice(8, 8), false)));
                    properties.Add(new("heif", "clean_aperture_offsets", Fraction(data.Slice(16, 8), true), Fraction(data.Slice(24, 8), true)));
                }
                children.Add(new("transform/" + children.Count.ToString(CultureInfo.InvariantCulture), properties));
            }
        }

        private static MediaPropertyValue Fraction(ReadOnlySpan<byte> bytes, bool signed)
        {
            long numerator = signed ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
            return MediaPropertyValue.FromRational(numerator, BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]));
        }

        private static string TypeName(uint type) => new([(char)(type >> 24), (char)((type >> 16) & 255), (char)((type >> 8) & 255), (char)(type & 255)]);
    }
}
