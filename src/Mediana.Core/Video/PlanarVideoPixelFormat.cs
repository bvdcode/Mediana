// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Three planar unsigned integer color channels with optional straight alpha. Samples above eight bits occupy the low bits
    /// of native-endian 16-bit words. YUV is ordered Y/Cb/Cr; RGB is ordered G/B/R. Alpha, when present, is the fourth full-resolution plane.</summary>
    public class PlanarVideoPixelFormat(string name, PlanarColorModel colorModel, int bitDepth,
        int horizontalSubsampling, int verticalSubsampling, int alphaBitDepth)
        : VideoPixelFormat(name, CreatePlanes(colorModel, bitDepth, horizontalSubsampling, verticalSubsampling, alphaBitDepth))
    {
        public PlanarVideoPixelFormat(string name, PlanarColorModel colorModel, int bitDepth,
            int horizontalSubsampling = 1, int verticalSubsampling = 1)
            : this(name, colorModel, bitDepth, horizontalSubsampling, verticalSubsampling, 0)
        {
        }

        /// <summary>Precision of an optional full-resolution straight alpha plane; zero means opaque.</summary>
        public int AlphaBitDepth { get; } = alphaBitDepth;
        public PlanarColorModel ColorModel { get; } = colorModel;
        public int BitDepth { get; } = bitDepth;
        public int HorizontalSubsampling { get; } = horizontalSubsampling;
        public int VerticalSubsampling { get; } = verticalSubsampling;

        protected override bool EqualsInterpretation(VideoPixelFormat other) =>
            other is PlanarVideoPixelFormat planar && ColorModel == planar.ColorModel &&
            BitDepth == planar.BitDepth && HorizontalSubsampling == planar.HorizontalSubsampling &&
            VerticalSubsampling == planar.VerticalSubsampling && AlphaBitDepth == planar.AlphaBitDepth;

        protected override int InterpretationHashCode() =>
            HashCode.Combine(ColorModel, BitDepth, HorizontalSubsampling, VerticalSubsampling, AlphaBitDepth);

        private static VideoPlaneFormat[] CreatePlanes(PlanarColorModel colorModel, int bitDepth, int horizontal, int vertical, int alphaDepth)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bitDepth, 8); ArgumentOutOfRangeException.ThrowIfGreaterThan(bitDepth, 16);
            if (horizontal is not (1 or 2 or 4))
            {
                throw new ArgumentOutOfRangeException(nameof(horizontal));
            }
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
            if (alphaDepth != 0 && (alphaDepth is < 8 or > 16))
            {
                throw new ArgumentOutOfRangeException(nameof(alphaDepth));
            }
            VideoPlaneFormat[] color = [new(1, 1, bytes), new(horizontal, vertical, bytes), new(horizontal, vertical, bytes)];
            return alphaDepth == 0 ? color : [.. color, new(1, 1, alphaDepth > 8 ? 2 : 1)];
        }
    }
}
