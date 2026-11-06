// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Imaging;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private async ValueTask ImageFillAsync(VectorPath path, XElement fill, Matrix3x2 transform)
        {
            XNamespace a = fill.Elements().FirstOrDefault()?.Name.Namespace ?? fill.Name.Namespace;
            XElement blip = fill.Element(a + "blip") ?? throw new InvalidDataException("Presentation picture fill has no image relationship.");
            PresentationPart part = scene.SourceOf(fill);
            string imagePart = part.Relationship(PresentationXml.RelationId(blip, "embed"), "image");
            (float x, float y, float width, float height) = path.GetBounds(token);
            if (width <= 0 || height <= 0) { return; }
            XElement? stretch = fill.Element(a + "stretch"), tile = fill.Element(a + "tile");
            if (stretch is not null && tile is not null) { throw new InvalidDataException("Presentation image fill cannot be both stretched and tiled."); }
            if (!PresentationXml.Boolean(fill, "rotWithShape", true) && (transform.M12 != 0 || transform.M21 != 0))
            { throw new NotSupportedException("Unrotated picture fills inside rotated shapes are not supported."); }
            XElement? crop = fill.Element(a + "srcRect");
            double left = PresentationXml.Percent(crop, "l"), right = PresentationXml.Percent(crop, "r");
            double top = PresentationXml.Percent(crop, "t"), bottom = PresentationXml.Percent(crop, "b");
            double sourceWidth = 1 - left - right, sourceHeight = 1 - top - bottom;
            if (sourceWidth <= 0 || sourceHeight <= 0) { throw new InvalidDataException("Invalid presentation image source rectangle."); }
            XElement? target = stretch?.Element(a + "fillRect");
            double dl = PresentationXml.Percent(target, "l"), dr = PresentationXml.Percent(target, "r");
            double dt = PresentationXml.Percent(target, "t"), db = PresentationXml.Percent(target, "b");
            if (1 - dl - dr <= 0 || 1 - dt - db <= 0) { throw new InvalidDataException("Invalid presentation image fill rectangle."); }
            double targetX = x + width * dl, targetY = y + height * dt;
            double targetWidth = width * (1 - dl - dr), targetHeight = height * (1 - dt - db);
            float opacity = 1;
            foreach (XElement alpha in blip.Elements(a + "alphaModFix"))
            {
                double amount = PresentationXml.Percent(alpha, "amt");
                if (amount < 0 || amount > 1) { throw new InvalidDataException("Invalid presentation image opacity."); }
                opacity *= (float)amount;
            }
            if (opacity == 0) { return; }
            double sx = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
            double sy = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
            int preferredWidth = stretch is null ? 0 : (int)Math.Clamp(Math.Ceiling(targetWidth * sx / sourceWidth), 1, int.MaxValue);
            int preferredHeight = stretch is null ? 0 : (int)Math.Clamp(Math.Ceiling(targetHeight * sy / sourceHeight), 1, int.MaxValue);
            VideoFrame image = await ReadImageAsync(imagePart, preferredWidth, preferredHeight).ConfigureAwait(false);
            canvas.Save();
            try
            {
                canvas.ClipPath(path, transform, cancellationToken: token);
                if (stretch is null)
                { TileImage(image, fill, tile, x, y, width, height, left, top, sourceWidth, sourceHeight, opacity, transform); }
                else
                {
                    double iw = targetWidth / sourceWidth, ih = targetHeight / sourceHeight;
                    Matrix3x2 placed = Matrix3x2.CreateScale((float)(iw / image.Format.Width), (float)(ih / image.Format.Height))
                        * Matrix3x2.CreateTranslation((float)(targetX - iw * left), (float)(targetY - ih * top)) * transform;
                    canvas.ClipPath(new VectorPath().Rectangle((float)targetX, (float)targetY, (float)targetWidth, (float)targetHeight), transform, cancellationToken: token);
                    canvas.DrawImage(image, placed, opacity, cancellationToken: token);
                }
            }
            finally { canvas.Restore(); }
        }

        private async ValueTask<VideoFrame> ReadImageAsync(string part, int width, int height)
        {
            (string Part, int Width, int Height) key = (part, width, height);
            if (_images.TryGetValue(key, out VideoFrame? cached)) { return cached; }
            ImageDecodeRequest request = new()
            {
                MaximumPixels = options.MaximumImagePixels,
                PreferredWidth = width == 0 ? null : width,
                PreferredHeight = height == 0 ? null : height,
                WarningHandler = options.WarningHandler
            };
            VideoFrame image = await ImageResources.DecodeAsync(document.Package.ReadPart(part, token), options.Images, request, allocator, options.ImageColorResolver, token).ConfigureAwait(false);
            bool stored = false;
            try { _images.Add(key, image); stored = true; return image; }
            finally { if (!stored) { image.Dispose(); } }
        }
    }
}
