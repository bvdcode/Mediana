// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Applies transformative properties in their item association order.
    /// </summary>
    /// <remarks>
    /// Rotations are counterclockwise quarter turns. HEIF 2022 mirror axis 0 reverses
    /// rows, and axis 1 reverses columns. Clean-aperture offsets locate its centre relative
    /// to the stored image centre; only exact integral sample rectangles are supported.
    /// Each result uses separate pixel storage and never modifies the source image.
    /// </remarks>
    internal static class ImageTransforms
    {
        public static RgbaImage Apply(RgbaImage image, HeifBox property, PixelBufferScope buffers)
        {
            if ((property.Type == HeifBox.Code("irot")) || (property.Type == HeifBox.Code("imir")))
            {
                return Orient(image, ImageOrientation.Read(image.Width, image.Height, property), buffers);
            }

            if (property.Type == HeifBox.Code("clap"))
            {
                ImageCrop crop = ImageCrop.Read(image.Width, image.Height, property);
                return Crop(image, crop.Left, crop.Top, crop.Width, crop.Height, buffers);
            }
            throw new InvalidDataException("Expected an image transformation.");
        }

        public static RgbaImage Orient(RgbaImage source, int turns, int? mirrorAxis, PixelBufferScope buffers)
        {
            return Orient(source, ImageOrientation.Create(source.Width, source.Height, turns, mirrorAxis), buffers);
        }

        private static RgbaImage Orient(RgbaImage source, ImageOrientation orientation, PixelBufferScope buffers)
        {
            Memory<byte> pixels = buffers.Allocate(checked(orientation.Width * orientation.Height * 4));
            orientation.CopyTile(source, pixels.Span, 0, 0, source.Width, source.Height);
            return new RgbaImage(orientation.Width, orientation.Height, pixels, source.ColorInformation);
        }

        private static RgbaImage Crop(RgbaImage source, int left, int top, int width, int height, PixelBufferScope buffers)
        {
            Memory<byte> pixels = buffers.Allocate(checked(width * height * 4));
            for (int y = 0; y < height; y++)
            {
                source.Pixels.Span.Slice(((top + y) * source.Stride) + (left * 4), width * 4)
                    .CopyTo(pixels.Span.Slice(y * width * 4, width * 4));
            }

            return new RgbaImage(width, height, pixels, source.ColorInformation);
        }
    }
}
