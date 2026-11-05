// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;

namespace Mediana.Codecs.Jpeg
{
    internal partial class JpegMetadata
    {
        private readonly List<MediaMetadata> inspection = [];

        internal void Inspect(byte marker, ReadOnlyMemory<byte> segment, CancellationToken token)
        {
            if (marker == 225 && segment.Span.StartsWith("Exif\0\0"u8))
            {
                inspection.Add(ExifMetadataReader.Read(segment[6..], token));
            }
            else if (marker == 226 && segment.Span.StartsWith("ICC_PROFILE\0"u8))
            {
                inspection.Add(new("ICC/" + segment.Span[12], [new("jpeg", "profile_part", (uint)segment.Span[12]),
                    new("jpeg", "profile_parts", (uint)segment.Span[13]), new("icc", "profile_fragment", MediaPropertyValue.FromBytes(segment.Span[14..]))]));
            }
            else if (marker == 254)
            {
                inspection.Add(new("comment", [new("jpeg", "comment", Encoding.Latin1.GetString(segment.Span))]));
            }
        }

        internal MediaMetadata Inspect(JpegDocument image)
        {
            List<MediaProperty> properties = [new("media", "codec", "jpeg"), new("jpeg", "stored_dimensions", image.Width, image.Height),
                new("jpeg", "progressive", image.Progressive), new("jpeg", "components", image.Components.Count),
                new("jpeg", "bit_depth", 8), new("jpeg", "has_icc_profile", HasIcc),
                new("media", "display_to_source_matrix", Orientation.A, Orientation.B, Orientation.C, Orientation.D)];
            if (Aspect is Core.Video.SampleAspectRatio aspect)
            {
                properties.Add(new("media", "sample_aspect_ratio", MediaPropertyValue.FromRational(aspect.Numerator, aspect.Denominator)));
            }
            if (AdobeColorTransform is int transform) { properties.Add(new("jpeg", "adobe_color_transform", transform)); }
            if (IsSrgb) { properties.Add(new("exif", "color_space", "sRGB")); }
            List<MediaMetadata> components = image.Components.Select((component, index) => new MediaMetadata("component/" + index,
                [new("jpeg", "identifier", (uint)component.Id), new("jpeg", "sampling_factors", component.Horizontal, component.Vertical)])).ToList();
            return new("image", properties, components.Concat(inspection));
        }
    }
}
