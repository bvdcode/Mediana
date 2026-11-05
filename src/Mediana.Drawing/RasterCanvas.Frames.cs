// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using System.Numerics;

namespace Mediana.Drawing
{
    public partial class RasterCanvas
    {
        /// <summary>Draws borrowed straight RGBA32 frame storage without copying it to a second image.
        /// The caller keeps the frame alive until drawing completes; samples are interpreted as sRGB.</summary>
        public void DrawImage(VideoFrame image, Matrix3x2 transform, float opacity = 1,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Format.PixelFormat != VideoPixelFormat.Rgba32) { throw new ArgumentException("Drawing requires straight RGBA32 pixels.", nameof(image)); }
            PlaneLayout plane = image.Planes[0];
            if (plane.Stride < 0) { throw new NotSupportedException("Drawing requires a positive RGBA row stride."); }
            DrawRgbaImage(image.Data[plane.Offset..], image.Format.Width, image.Format.Height, plane.Stride,
                transform, opacity, blendMode, cancellationToken);
        }
    }
}
