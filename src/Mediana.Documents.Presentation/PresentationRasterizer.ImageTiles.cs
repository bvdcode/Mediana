// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Drawing;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private void TileImage(VideoFrame image, XElement fill, XElement? tile, double x, double y, double width, double height,
            double sourceLeft, double sourceTop, double sourceWidth, double sourceHeight, float opacity, Matrix3x2 transform)
        {
            double dpi = PresentationXml.Number(fill, "dpi");
            if (dpi < 0 || dpi > uint.MaxValue || dpi != Math.Truncate(dpi)) { throw new InvalidDataException("Invalid presentation image DPI."); }
            if (dpi == 0) { dpi = options.ImageDpi; }
            double sx = PresentationXml.Percent(tile, "sx", 1), sy = PresentationXml.Percent(tile, "sy", 1);
            double tileWidth = image.Format.Width * sourceWidth * 72 / dpi * sx;
            double tileHeight = image.Format.Height * sourceHeight * 72 / dpi * sy;
            if (!double.IsFinite(tileWidth) || !double.IsFinite(tileHeight) || tileWidth <= 0 || tileHeight <= 0)
            { throw new InvalidDataException("Invalid presentation image tile scale."); }
            (double horizontal, double vertical) = (string?)tile?.Attribute("algn") switch
            {
                null or "tl" => (0, 0),
                "t" => (.5, 0),
                "tr" => (1, 0),
                "l" => (0, .5),
                "ctr" => (.5, .5),
                "r" => (1, .5),
                "bl" => (0, 1),
                "b" => (.5, 1),
                "br" => (1, 1),
                _ => throw new InvalidDataException("Invalid presentation image tile alignment.")
            };
            (bool flipX, bool flipY) = (string?)tile?.Attribute("flip") switch
            {
                null or "none" => (false, false),
                "x" => (true, false),
                "y" => (false, true),
                "xy" => (true, true),
                _ => throw new InvalidDataException("Invalid presentation image tile flip.")
            };
            double originX = x + (width - tileWidth) * horizontal + PresentationXml.Points(tile, "tx");
            double originY = y + (height - tileHeight) * vertical + PresentationXml.Points(tile, "ty");
            PlaneLayout plane = image.Planes[0];
            ReadOnlyMemory<byte> data = image.Data[plane.Offset..];
            canvas.DrawShader(Sample, transform, cancellationToken: token);
            Rgba32 Sample(Vector2 point)
            {
                double px = Coordinate((point.X - originX) / tileWidth, flipX);
                double py = Coordinate((point.Y - originY) / tileHeight, flipY);
                int ix = (int)Math.Floor((sourceLeft + px * sourceWidth) * image.Format.Width);
                int iy = (int)Math.Floor((sourceTop + py * sourceHeight) * image.Format.Height);
                if ((uint)ix >= image.Format.Width || (uint)iy >= image.Format.Height) { return default; }
                int index = iy * plane.Stride + ix * 4;
                ReadOnlySpan<byte> pixel = data.Span.Slice(index, 4);
                return new(pixel[0], pixel[1], pixel[2], (byte)Math.Clamp(MathF.Round(pixel[3] * opacity), 0, 255));
            }
        }

        private static double Coordinate(double position, bool flip)
        {
            if (!double.IsFinite(position)) { throw new InvalidDataException("Presentation tile coordinates exceed the numeric range."); }
            double index = Math.Floor(position), fraction = position - index;
            if (flip && Math.Abs(index % 2) == 1) { fraction = 1 - fraction; }
            return Math.Min(fraction, Math.BitDecrement(1d));
        }
    }
}
