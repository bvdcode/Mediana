// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class RgbPrimariesTests
    {
        [Theory]
        [InlineData(ColorPrimaries.Bt709, 0.25, 0.5, 0.75)]
        [InlineData(ColorPrimaries.Bt470Bg, 0.238989197809291, 0.5, 0.747051655428999)]
        [InlineData(ColorPrimaries.Smpte170M, 0.267683628898413, 0.499665672803993, 0.751903237386527)]
        [InlineData(ColorPrimaries.Bt2020, 0.0666647836429202, 0.529050262979305, 0.784220106179454)]
        [InlineData(ColorPrimaries.DisplayP3, 0.19376495592986, 0.510514238677422, 0.779477788682825)]
        [InlineData(ColorPrimaries.Ebu3213, 0.244010654124276, 0.503296244328963, 0.751245346422523)]
        public void LinearColorVectorsMatchRationalChromaticityCalculationsAndPreserveWhite(ColorPrimaries primaries,
            double expectedRed, double expectedGreen, double expectedBlue)
        {
            RgbMatrix matrix = RgbPrimariesTransform.ToSrgb(primaries);
            (double red, double green, double blue) = matrix.Transform(0.25, 0.5, 0.75);
            Assert.Equal(expectedRed, red, 12);
            Assert.Equal(expectedGreen, green, 12);
            Assert.Equal(expectedBlue, blue, 12);
            (double whiteRed, double whiteGreen, double whiteBlue) = matrix.Transform(1, 1, 1);
            Assert.Equal(1, whiteRed, 12);
            Assert.Equal(1, whiteGreen, 12);
            Assert.Equal(1, whiteBlue, 12);
            Assert.Equal((0.0, 0.0, 0.0), matrix.Transform(0, 0, 0));
        }

        [Fact]
        public void UnknownAndDifferentWhitePointPrimariesAreExplicitlyUnsupported()
        {
            Assert.Throws<NotSupportedException>(() => RgbPrimariesTransform.ToSrgb(ColorPrimaries.Unspecified));
            Assert.Throws<NotSupportedException>(() => RgbPrimariesTransform.ToSrgb(ColorPrimaries.DciP3));
            Assert.Throws<NotSupportedException>(() => RgbPrimariesTransform.ToSrgb(ColorPrimaries.Bt470M));
            Assert.Throws<ArgumentOutOfRangeException>(() => RgbPrimariesTransform.ToSrgb((ColorPrimaries)int.MaxValue));
        }
    }
}
