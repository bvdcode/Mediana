// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats.Metadata;

namespace Mediana.Codecs.Png
{
    internal static class PngImageMetadata
    {
        internal static MediaMetadata Read(PngImageData image, CancellationToken token)
        {
            List<MediaProperty> properties = [new("media", "codec", "png"), new("png", "bit_depth", image.Depth),
                new("png", "color_type", image.ColorType), new("png", "components", image.Channels),
                new("png", "adam7_interlaced", image.Interlaced), new("png", "has_animation", image.Animated),
                new("png", "has_exif", image.HasExif), new("png", "stored_dimensions", image.Width, image.Height)];
            if (!image.Palette.IsEmpty) { properties.Add(new("png", "palette_entries", image.Palette.Length / 3)); }
            if (!image.Transparency.IsEmpty) { properties.Add(new("png", "transparency", MediaPropertyValue.FromBytes(image.Transparency.Span))); }
            if (image.Aspect is SampleAspectRatio aspect)
            {
                properties.Add(new("media", "sample_aspect_ratio", MediaPropertyValue.FromRational(aspect.Numerator, aspect.Denominator)));
            }
            switch (image.ColorDescription)
            {
                case CicpColorDescription cicp:
                    properties.AddRange([new("cicp", "primaries", cicp.Primaries), new("cicp", "transfer", cicp.Transfer), new("cicp", "matrix", cicp.Matrix)]);
                    if (cicp.FullRange is bool full) { properties.Add(new("cicp", "full_range", full)); }
                    break;
                case PngColorDescription color:
                    if (color.Gamma is uint gamma) { properties.Add(new("png", "gamma", MediaPropertyValue.FromRational(gamma, 100000))); }
                    if (color.Chromaticities.Count != 0)
                    {
                        properties.Add(new("png", "chromaticities", color.Chromaticities.Select(value => MediaPropertyValue.FromRational(value, 100000)).ToArray()));
                    }
                    properties.Add(new("png", "has_icc_profile", color.HasEmbeddedIccProfile));
                    break;
                case null:
                    if (image.Color == VideoColorInfo.Srgb) { properties.Add(new("png", "color_space", "sRGB")); }
                    break;
                default:
                    throw new NotSupportedException("PNG source color metadata has no inspector.");
            }
            List<MediaMetadata> children = [];
            if (!image.Exif.IsEmpty) { children.Add(ExifMetadataReader.Read(image.Exif, token)); }
            return new("image", properties, children);
        }
    }
}
