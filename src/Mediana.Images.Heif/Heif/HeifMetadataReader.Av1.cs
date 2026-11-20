// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Formats.Video;

namespace Mediana.Images.Heif.Container
{
    internal static partial class HeifMetadataReader
    {
        private static void AddAv1Encoding(HeifContainer container, List<MediaMetadata> children, CancellationToken token)
        {
            IEnumerable<IGrouping<IReadOnlyList<PropertyAssociation>, HeifItem>> groups = container.Items.Values
                .Where(item => item.Type == HeifBox.Code("av01")).GroupBy(item => item.Associations, new PropertyAssociationComparer());
            int index = 0;
            foreach (IGrouping<IReadOnlyList<PropertyAssociation>, HeifItem> group in groups)
            {
                token.ThrowIfCancellationRequested();
                ImageProperties source = ImageProperties.Read(container, group.First());
                Av1CodecConfigurationRecord configuration = source.Av1Configuration ?? throw new InvalidDataException("AV1 image configuration is missing.");
                List<MediaProperty> encoding = [new("media", "codec", "av1"), new("media", "source_dimensions", source.Width, source.Height),
                    new("heif", "item_count", group.Count()), new("av1", "profile", configuration.Profile),
                    new("av1", "level", configuration.Level), new("av1", "high_tier", configuration.HighTier),
                    new("av1", "bit_depth", configuration.BitDepth), new("av1", "monochrome", configuration.Monochrome),
                    new("av1", "subsampling_x", configuration.SubsamplingX), new("av1", "subsampling_y", configuration.SubsamplingY),
                    new("av1", "chroma_position", configuration.ChromaPosition)];
                AddProperties(source, encoding);
                children.Add(new("av1/encoding/" + index++, encoding));
            }
        }

        private static void AddImageRelationships(HeifContainer container, List<MediaMetadata> children, CancellationToken token)
        {
            foreach (HeifItem item in container.Items.Values)
            {
                token.ThrowIfCancellationRequested();
                foreach (KeyValuePair<uint, IReadOnlyList<uint>> reference in item.References)
                {
                    List<MediaProperty> properties = [new("heif", "item_id", item.Id), new("heif", "reference_type", TypeName(reference.Key)),
                        new("heif", "targets", reference.Value.Select(id => (MediaPropertyValue)id).ToArray())];
                    if (reference.Key == HeifBox.Code("thmb") || reference.Key == HeifBox.Code("auxl"))
                    {
                        ImageProperties source = ImageProperties.Read(container, item);
                        (int width, int height) = source.GetDisplaySize(token);
                        properties.Add(new("image", "width", width)); properties.Add(new("image", "height", height));
                        properties.Add(new("heif", "item_type", TypeName(item.Type)));
                        AddProperties(source, properties);
                    }
                    children.Add(new("item/" + item.Id + "/reference/" + TypeName(reference.Key), properties));
                }
            }
        }
    }
}
