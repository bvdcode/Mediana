// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal partial class SvgSceneBuilder
    {
        private readonly Dictionary<string, VideoFrame?> _images = new(StringComparer.Ordinal);
        private VideoFrame? Image(string? href)
        {
            if (href is null)
            {
                Warn("Missing SVG image href");
                return null;
            }

            if (_images.TryGetValue(href, out VideoFrame? cached))
            {
                return cached;
            }

            VideoFrame? image = null;
            if (href.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase))
            {
                int comma = href.IndexOf(',');
                if (comma < 0 || !href[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Embedded PNG must use a base64 data URI.");
                }

                PngImageDecoder decoder = new(new() { MaximumPixels = _limits.MaximumImagePixels, MaximumEncodedBytes = _limits.MaximumInputBytes });
                image = decoder.Decode(Convert.FromBase64String(href[(comma + 1)..]), new(), _imageAllocator, _token);
            }
            else if (_limits.ImageResolver?.Invoke(href) is { } resolved)
            {
                if ((long)resolved.Width * resolved.Height > _limits.MaximumImagePixels)
                {
                    throw new InvalidDataException("Resolved SVG image exceeds MaximumImagePixels.");
                }

                image = resolved.ToVideoFrame(_imageAllocator, _token);
            }
            else
            {
                Warn("Unresolved SVG image resource");
            }

            _images.Add(href, image);
            return image;
        }

        private Matrix3x2 ImagePlacement(XElement element, VideoFrame image)
        {
            float width = Length(element, "width", image.Format.Width, _width), height = Length(element, "height", image.Format.Height, _height);
            if (width < 0 || height < 0)
            {
                throw new InvalidDataException("Negative SVG image dimensions.");
            }

            return SvgRenderer.Viewport((0, 0, image.Format.Width, image.Format.Height), width, height, (string?)element.Attribute("preserveAspectRatio") ?? "xMidYMid meet") * Matrix3x2.CreateTranslation(Length(element, "x", 0, _width), Length(element, "y", 0, _height));
        }
    }
}
