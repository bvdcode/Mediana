// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.OpenExr
{
    internal static class ExrImageMetadata
    {
        internal static ImageInfo Read(ExrDocument document, CancellationToken token)
        {
            List<MediaMetadata> parts = [];
            for (int index = 0; index < document.Parts.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                ExrHeader part = document.Parts[index];
                List<MediaProperty> properties = [new("exr", "part_index", index), new("exr", "part_type", part.Type), new("exr", "name", part.Name),
                    new("exr", "data_window", part.DataWindow.Left, part.DataWindow.Top, part.DataWindow.Right, part.DataWindow.Bottom),
                    new("exr", "display_window", part.DisplayWindow.Left, part.DisplayWindow.Top, part.DisplayWindow.Right, part.DisplayWindow.Bottom),
                    new("exr", "pixel_aspect_ratio", part.PixelAspectRatio), new("exr", "compression", part.Compression),
                    new("exr", "chunk_count", part.ChunkCount), new("exr", "line_order", part.LineOrder)];
                List<MediaMetadata> children = [];
                foreach (ExrChannel channel in part.Channels)
                {
                    children.Add(new("channel/" + channel.Name, [new("exr", "sample_type", channel.Type), new("exr", "perceptually_linear", channel.PerceptuallyLinear),
                        new("exr", "sampling_x", channel.SamplingX), new("exr", "sampling_y", channel.SamplingY)]));
                }
                foreach (KeyValuePair<string, ExrAttribute> attribute in part.Attributes)
                {
                    token.ThrowIfCancellationRequested();
                    if (attribute.Key is "channels" or "compression" or "dataWindow" or "displayWindow" or "pixelAspectRatio" or "lineOrder" or "chunkCount" or "name" or "type") { continue; }
                    ExrAttribute value = attribute.Value;
                    List<MediaProperty> native = [new("exr", "type", value.Type)];
                    MediaPropertyValue[] values = Values(value, token);
                    native.Add(new("exr", "value", values));
                    children.Add(new("attribute/" + attribute.Key, native));
                }
                parts.Add(new("part/" + index, properties, children));
            }
            ExrWindow first = document.Parts[0].DataWindow;
            return new("openexr", first.Width, first.Height, ImageDimensionUnit.Pixels, pageCount: parts.Count,
                metadata: new("image", [new("exr", "multipart", document.Multipart)], parts));
        }

        private static MediaPropertyValue[] Values(ExrAttribute attribute, CancellationToken token)
        {
            ReadOnlySpan<byte> bytes = attribute.Data.Span;
            if (attribute.Type == "string") { return [new UTF8Encoding(false, true).GetString(bytes)]; }
            int components = attribute.Type switch
            {
                "float" or "int" => 1,
                "v2f" or "v2i" => 2,
                "v3f" or "v3i" => 3,
                "box2f" or "box2i" => 4,
                "chromaticities" => 8,
                "m33f" => 9,
                "m44f" => 16,
                _ => 0
            };
            if (components == 0 || bytes.Length != components * 4) { return [MediaPropertyValue.FromBytes(bytes)]; }
            bool integer = attribute.Type is "int" or "v2i" or "v3i" or "box2i";
            MediaPropertyValue[] values = new MediaPropertyValue[components];
            for (int i = 0; i < components; i++)
            {
                token.ThrowIfCancellationRequested();
                if (integer) { values[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * 4)..]); }
                else
                {
                    float value = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..]);
                    if (!float.IsFinite(value)) { return [MediaPropertyValue.FromBytes(bytes)]; }
                    values[i] = value;
                }
            }
            return values;
        }
    }
}
