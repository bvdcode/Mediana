// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using Mediana.Core.Memory;

namespace Mediana.Drawing
{
    /// <summary>Owns writable row-major RGBA8 pixels using sRGB color and straight alpha.
    /// Borrowed pixel memory must not be used after disposal or publication. This owner is not thread-safe.</summary>
    public partial class PixelBuffer : IDisposable
    {
        private readonly WritableMediaBuffer _pixels;
        internal MediaBufferAllocator Allocator { get; }

        /// <summary>Gets the image width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the image height in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets writable pixel memory in R, G, B, A channel order.</summary>
        public Memory<byte> Pixels => _pixels.Memory;

        /// <summary>Creates a transparent black image.</summary>
        public PixelBuffer(int width, int height) : this(width, height, new MediaBufferAllocator(long.MaxValue))
        {
        }

        /// <summary>Creates a transparent black image charged to the supplied live-buffer budget.</summary>
        public PixelBuffer(int width, int height, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            long pixelCount = (long)width * height;
            if (pixelCount > Array.MaxLength / 4)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The pixel buffer exceeds the supported array size.");
            }

            Allocator = allocator;
            Width = width;
            Height = height;
            _pixels = allocator.Allocate((int)pixelCount * 4);
        }

        /// <summary>Gets or sets a pixel using zero-based coordinates.</summary>
        public Rgba32 this[int x, int y]
        {
            get
            {
                int offset = GetOffset(x, y);
                Span<byte> pixels = Pixels.Span;
                return new Rgba32(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
            }
            set
            {
                int offset = GetOffset(x, y);
                Span<byte> pixels = Pixels.Span;
                pixels[offset] = value.R;
                pixels[offset + 1] = value.G;
                pixels[offset + 2] = value.B;
                pixels[offset + 3] = value.A;
            }
        }

        /// <summary>Fills the image with a color.</summary>
        public void Clear(Rgba32 color)
        {
            Span<byte> pixels = Pixels.Span;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = color.R;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.B;
                pixels[offset + 3] = color.A;
            }
        }

        /// <summary>Releases unpublished storage. Published frames keep their own leases.</summary>
        public void Dispose() => _pixels.Dispose();

        private int GetOffset(int x, int y)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
            return ((y * Width) + x) * 4;
        }
    }
}
