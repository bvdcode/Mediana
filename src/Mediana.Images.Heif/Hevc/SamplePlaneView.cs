// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Borrows contiguous component rows for synchronous filtering within a picture's lifetime.
    /// </summary>
    internal readonly ref struct SamplePlaneView<TSample> where TSample : unmanaged
    {
        private readonly Span<TSample> samples;

        public SamplePlaneView(Span<TSample> samples, int width, int height)
        {
            if ((width <= 0) || (height <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            this.samples = samples.Slice(0, checked(width * height));
            Width = width;
            Height = height;
        }

        public Span<TSample> Samples => samples;
        public int Width { get; }
        public int Height { get; }
        public Span<TSample> Row(int y) => samples.Slice(checked(y * Width), Width);
    }
}
