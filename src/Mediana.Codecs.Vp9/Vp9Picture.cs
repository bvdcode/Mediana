// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Vp9
{
    internal class Vp9Picture(Vp9PictureGeometry geometry, MediaBuffer pixels) : IDisposable
    {
        internal Vp9PictureGeometry Geometry { get; } = geometry;
        internal MediaBuffer Pixels { get; } = pixels;
        internal int Stride => (Geometry.Width + 63) / 64 * 64;
        internal int PaddedHeight => (Geometry.Height + 63) / 64 * 64;
        internal Vp9Picture Retain() => new(Geometry, Pixels.Retain());
        public void Dispose() => Pixels.Dispose();
    }
}
