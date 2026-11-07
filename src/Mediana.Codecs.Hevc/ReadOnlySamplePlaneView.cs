// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly ref struct ReadOnlySamplePlaneView<TSample> where TSample : unmanaged
    {
        private readonly ReadOnlySpan<TSample> samples;

        public ReadOnlySamplePlaneView(ReadOnlySpan<TSample> samples, int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            this.samples = samples[..checked(width * height)];
            Width = width; Height = height;
        }

        public ReadOnlySpan<TSample> Samples => samples;
        public int Width { get; }
        public int Height { get; }
        public ReadOnlySpan<TSample> Row(int y) => samples.Slice(checked(y * Width), Width);
    }
}
