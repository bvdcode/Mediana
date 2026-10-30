// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class VideoColorDescriptionTests
    {
        private static readonly VideoColorInfo Codec = new(ColorPrimaries.Smpte170M, TransferFunction.Gamma22,
            MatrixCoefficients.Bt601, ColorRange.Full, ChromaLocation.Center);

        [Theory]
        [InlineData(null, ColorRange.Full)]
        [InlineData(false, ColorRange.Limited)]
        [InlineData(true, ColorRange.Full)]
        public void SourceDescriptionOverridesColorButOnlyDeclaredRange(bool? fullRange, ColorRange range)
        {
            CicpColorDescription description = new(1, 13, 1, fullRange);
            VideoFormat format = new(16, 16, VideoPixelFormat.Yuv420P8, Codec, colorDescription: description);
            Assert.Equal(new VideoColorInfo(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Bt709,
                range, ChromaLocation.Center), format.GetColorInterpretation());
            Assert.Same(Codec, format.Color);
            Assert.Same(description, format.ColorDescription);
            Assert.Equal(1, description.Primaries);
            Assert.Equal(13, description.Transfer);
            Assert.Equal(1, description.Matrix);
        }

        [Fact]
        public void UnspecifiedContainerLabelsRemainUnspecifiedAndAbsencePreservesCodecLabels()
        {
            CicpColorDescription unspecified = new(2, 2, 2);
            Assert.Equal(new VideoColorInfo(Range: ColorRange.Full, ChromaLocation: ChromaLocation.Center), unspecified.Resolve(Codec));
            Assert.Same(Codec, new VideoFormat(16, 16, VideoPixelFormat.Yuv420P8, Codec).GetColorInterpretation());
        }

        [Theory]
        [InlineData(0, 1, 1)]
        [InlineData(3, 1, 1)]
        [InlineData(1, 255, 1)]
        [InlineData(1, 1, 65535)]
        public void UnsupportedCodesAreRetainedAndNeverBecomeMissingLabels(int primaries, int transfer, int matrix)
        {
            CicpColorDescription description = new(primaries, transfer, matrix);
            Assert.Equal(primaries, description.Primaries);
            Assert.Equal(transfer, description.Transfer);
            Assert.Equal(matrix, description.Matrix);
            Assert.Throws<NotSupportedException>(() => description.Resolve(Codec));
        }

        [Theory]
        [InlineData(-1, 1, 1)]
        [InlineData(1, 65536, 1)]
        [InlineData(1, 1, -1)]
        public void CodesMustFitUnsignedContainerFields(int primaries, int transfer, int matrix)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CicpColorDescription(primaries, transfer, matrix));
        }

        [Fact]
        public void IccBytesAreOwnedAndRequireAnExplicitSupportedTransform()
        {
            byte[] input = [1, 2, 3, 4];
            IccColorDescription description = new(input, true);
            input[0] = 9;
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, description.Profile.ToArray());
            Assert.True(description.Restricted);
            Assert.Throws<NotSupportedException>(() => description.Resolve(Codec));
            Assert.Throws<ArgumentException>(() => new IccColorDescription([], false));
        }

        [Fact]
        public void StreamColorDescriptionsRequireVideo()
        {
            CicpColorDescription description = new(1, 1, 1, false);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "h264", StreamFormats.Avc, new TimeBase(1, 1000),
                colorDescription: description);
            Assert.Same(description, stream.ColorDescription);
            Assert.Throws<ArgumentException>(() => new EncodedStreamInfo(0, MediaKind.Audio, "aac", StreamFormats.Aac,
                new TimeBase(1, 48000), colorDescription: description));
        }

        [Theory]
        [InlineData(0, MatrixCoefficients.Rgb)]
        [InlineData(1, MatrixCoefficients.Bt709)]
        [InlineData(5, MatrixCoefficients.Bt601)]
        [InlineData(6, MatrixCoefficients.Bt601)]
        [InlineData(9, MatrixCoefficients.Bt2020NonConstant)]
        [InlineData(14, MatrixCoefficients.ICtCp)]
        [InlineData(255, MatrixCoefficients.Unspecified)]
        public void CommonMatrixMappingPreservesCicpMeaning(int code, MatrixCoefficients expected)
        {
            Assert.Equal(expected, CicpColorimetry.GetMatrix(code));
        }
    }
}
