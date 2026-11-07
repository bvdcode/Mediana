// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Three planar unsigned integer channels. Samples above eight bits occupy the low bits
    /// of native-endian 16-bit words. YUV is ordered Y/Cb/Cr; RGB is ordered G/B/R.</summary>
    public class PlanarVideoPixelFormat(string name, PlanarColorModel colorModel, int bitDepth,
        int horizontalSubsampling = 1, int verticalSubsampling = 1)
        : VideoPixelFormat(name, CreatePlanes(colorModel, bitDepth, horizontalSubsampling, verticalSubsampling))
    {
        public PlanarColorModel ColorModel { get; } = colorModel;
        public int BitDepth { get; } = bitDepth;
        public int HorizontalSubsampling { get; } = horizontalSubsampling;
        public int VerticalSubsampling { get; } = verticalSubsampling;

        private static VideoPlaneFormat[] CreatePlanes(PlanarColorModel colorModel, int bitDepth, int horizontal, int vertical)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bitDepth, 8); ArgumentOutOfRangeException.ThrowIfGreaterThan(bitDepth, 16);
            ArgumentOutOfRangeException.ThrowIfLessThan(horizontal, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(horizontal, 2);
            ArgumentOutOfRangeException.ThrowIfLessThan(vertical, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(vertical, 2);
            switch (colorModel)
            {
                case PlanarColorModel.Yuv: break;
                case PlanarColorModel.Gbr:
                    if (horizontal != 1 || vertical != 1) { throw new ArgumentException("Planar RGB channels must have equal dimensions."); }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(colorModel));
            }
            int bytes = bitDepth <= 8 ? 1 : 2;
            return [new(1, 1, bytes), new(horizontal, vertical, bytes), new(horizontal, vertical, bytes)];
        }
    }
}
