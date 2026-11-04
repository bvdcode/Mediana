// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegComponent(byte id, int horizontal, int vertical, int quantization, int reduction)
    {
        internal byte Id { get; } = id;
        internal int Horizontal { get; } = horizontal;
        internal int Vertical { get; } = vertical;
        internal int Quantization { get; } = quantization;
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal int Stride { get; set; }
        internal int PaddedHeight { get; set; }
        internal int Reduction { get; } = reduction;
        internal int SampleWidth { get; set; }
        internal int SampleHeight { get; set; }
        internal int SampleStride { get; set; }
        internal int SamplePaddedHeight { get; set; }
        internal bool Decoded { get; set; }
        internal WritableMediaBuffer? Samples { get; set; }
        internal JpegProgression? Progression { get; set; }
    }
}
