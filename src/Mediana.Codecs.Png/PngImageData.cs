// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    internal class PngImageData(int width, int height, int depth, int colorType, bool interlaced)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int Depth { get; } = depth;
        public int ColorType { get; } = colorType;
        public bool Interlaced { get; } = interlaced;
        public ReadOnlyMemory<byte> Palette { get; set; }
        public ReadOnlyMemory<byte> Transparency { get; set; }
        public int DataStart { get; set; } = -1;
        public int DataEnd { get; set; }
        public SampleAspectRatio? Aspect { get; set; } = SampleAspectRatio.Square;
        public VideoColorInfo Color { get; set; } = new(Matrix: MatrixCoefficients.Rgb, Range: ColorRange.Full);
        public VideoColorDescription? ColorDescription { get; set; }
        public bool Animated { get; set; }
        public bool HasExif { get; set; }
        public ReadOnlyMemory<byte> Exif { get; set; }
        public int Channels => ColorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException("Invalid PNG color type.")
        };
    }
}
