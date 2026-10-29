// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Contains a caller-owned, tightly packed RGBA8 image with rows ordered from top to bottom.
    /// </summary>
    public class RgbaImage
    {
        private readonly ReadOnlyMemory<byte> pixels;

        internal RgbaImage(int width, int height, ReadOnlyMemory<byte> pixels, ImageColorInformation colorInformation)
        {
            if ((width <= 0) || (height <= 0) || (pixels.Length != checked(width * height * 4)))
            {
                throw new ArgumentException("Pixel buffer does not match the image dimensions.", nameof(pixels));
            }

            Width = width;
            Height = height;
            this.pixels = pixels;
            ColorInformation = colorInformation;
        }

        /// <summary>
        /// Gets the primaries and transfer function retained by the RGB bytes.
        /// </summary>
        public ImageColorInformation ColorInformation { get; }

        /// <summary>
        /// Gets the visible image width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the visible image height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the number of bytes per row, without padding.
        /// </summary>
        public int Stride => checked(Width * 4);

        /// <summary>
        /// Gets the unpremultiplied red, green, blue and alpha bytes owned by this image.
        /// </summary>
        /// <remarks>
        /// A channel is addressed by y * Stride + x * 4 + channel, with x increasing rightwards
        /// and y increasing downwards. Each channel occupies one byte. The decoder does not
        /// reuse this buffer for other images or subsequent calls.
        /// </remarks>
        public ReadOnlyMemory<byte> Pixels => pixels;

        /// <summary>
        /// Copies all pixel bytes into caller storage without retaining the destination.
        /// </summary>
        /// <param name="destination">A buffer with room for at least Stride * Height bytes.</param>
        /// <exception cref="ArgumentException">The destination is smaller than the image buffer.</exception>
        public void CopyTo(Span<byte> destination) => pixels.Span.CopyTo(destination);
    }
}
