// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Images.Heif.Container;

namespace Mediana.Images.Heif
{
    /// <summary>Inspects AVIF primary-image metadata and original AV1 thumbnail item payloads without decoding pixels.</summary>
    public class AvifImageInspector(HeicDecodeOptions? options = null) : IImageInspector, IImagePreviewReader
    {
        private readonly HeicDecodeOptions limits = options ?? HeicDecodeOptions.Default;
        public string ImplementationId => "avif.managed.metadata";
        public int ProbeLength => 512;
        public bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null) =>
            HeifFileType.ContainsBrand(prefix, [HeifBox.Code("avif"), HeifBox.Code("avis")]);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            HeifContainer container = ReadContainer(encoded, cancellationToken);
            ImageProperties properties = ImageProperties.Read(container, container.Items[container.PrimaryItemId]);
            (int width, int height) = properties.GetDisplaySize(cancellationToken);
            return ValueTask.FromResult(new ImageInfo("avif", width, height, ImageDimensionUnit.Pixels,
                metadata: HeifMetadataReader.Read(container, properties, cancellationToken)));
        }

        /// <summary>Returns original AV1 OBU payloads, identified as av1, with their av1C bytes in av1.configuration.
        /// These payloads are elementary coded image items, not standalone AVIF containers.</summary>
        public ValueTask<IReadOnlyList<EncodedImagePreview>> ReadPreviewsAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            HeifContainer container = ReadContainer(encoded, cancellationToken);
            List<EncodedImagePreview> previews = [];
            foreach (HeifItem item in container.Items.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.References.TryGetValue(HeifBox.Code("thmb"), out IReadOnlyList<uint>? targets)
                    || !targets.Contains(container.PrimaryItemId) || item.Type != HeifBox.Code("av01")) { continue; }
                ImageProperties source = ImageProperties.Read(container, item);
                if (source.Av1Configuration is null) { throw new InvalidDataException("AV1 thumbnail configuration is missing."); }
                List<MediaProperty> properties = [new("heif", "item_id", item.Id),
                    new("av1", "configuration", MediaPropertyValue.FromBytes(source.Av1ConfigurationBytes.Span))];
                HeifMetadataReader.AddProperties(source, properties);
                List<MediaMetadata> transforms = [];
                HeifMetadataReader.AddTransforms(source, transforms);
                (int width, int height) = source.GetDisplaySize(cancellationToken);
                previews.Add(new(new("av1", width, height, ImageDimensionUnit.Pixels, metadata: new("image", properties, transforms)), container.ReadData(item.Id)));
            }
            return ValueTask.FromResult<IReadOnlyList<EncodedImagePreview>>(previews.AsReadOnly());
        }

        private HeifContainer ReadContainer(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanRead(encoded.Span)) { throw new InvalidDataException("AVIF file brand is missing."); }
            HeifContainer container = new HeifContainerReader(limits).Read(encoded);
            token.ThrowIfCancellationRequested();
            return container;
        }
    }
}
