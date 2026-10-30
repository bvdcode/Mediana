// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class PreviewColorPolicyTests
    {
        [Theory]
        [InlineData(640, 480, ColorPrimaries.Smpte170M, MatrixCoefficients.Bt601)]
        [InlineData(720, 480, ColorPrimaries.Smpte170M, MatrixCoefficients.Bt601)]
        [InlineData(720, 576, ColorPrimaries.Bt470Bg, MatrixCoefficients.Bt601)]
        [InlineData(1024, 576, ColorPrimaries.Bt470Bg, MatrixCoefficients.Bt601)]
        [InlineData(1280, 720, ColorPrimaries.Bt709, MatrixCoefficients.Bt709)]
        [InlineData(1920, 1080, ColorPrimaries.Bt709, MatrixCoefficients.Bt709)]
        public void MissingSdrLabelsUseDocumentedGeometryProfiles(int width, int height, ColorPrimaries primaries, MatrixCoefficients matrix)
        {
            VideoColorInfo declared = new(Range: ColorRange.Full, ChromaLocation: ChromaLocation.Center);
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8, declared, SampleAspectRatio.Square);
            VideoColorInfo resolved = PreviewColorPolicy.Resolve(format);
            Assert.Equal(primaries, resolved.Primaries);
            Assert.Equal(TransferFunction.Bt709, resolved.Transfer);
            Assert.Equal(matrix, resolved.Matrix);
            Assert.Equal(declared.Range, resolved.Range);
            Assert.Equal(declared.ChromaLocation, resolved.ChromaLocation);
            Assert.Same(declared, format.Color);
        }

        [Theory]
        [InlineData(MatrixCoefficients.Bt709, ColorPrimaries.Bt709)]
        [InlineData(MatrixCoefficients.Bt601, ColorPrimaries.Smpte170M)]
        [InlineData(MatrixCoefficients.Bt2020NonConstant, ColorPrimaries.Bt2020)]
        public void DeclaredMatrixDeterminesMissingPrimariesBeforeResolution(MatrixCoefficients matrix, ColorPrimaries primaries)
        {
            VideoColorInfo declared = new(Matrix: matrix, Transfer: TransferFunction.Gamma22);
            VideoColorInfo resolved = PreviewColorPolicy.Resolve(new VideoFormat(96, 64, VideoPixelFormat.Yuv420P8, declared));
            Assert.Equal(primaries, resolved.Primaries);
            Assert.Equal(matrix, resolved.Matrix);
            Assert.Equal(TransferFunction.Gamma22, resolved.Transfer);
        }

        [Theory]
        [InlineData(ColorPrimaries.Bt709, MatrixCoefficients.Bt709)]
        [InlineData(ColorPrimaries.Bt470Bg, MatrixCoefficients.Bt601)]
        [InlineData(ColorPrimaries.Smpte170M, MatrixCoefficients.Bt601)]
        [InlineData(ColorPrimaries.Bt2020, MatrixCoefficients.Bt2020NonConstant)]
        public void DeclaredPrimariesDetermineMissingMatrixBeforeResolution(ColorPrimaries primaries, MatrixCoefficients matrix)
        {
            VideoColorInfo declared = new(Primaries: primaries, Transfer: TransferFunction.Gamma22);
            VideoColorInfo resolved = PreviewColorPolicy.Resolve(new VideoFormat(1920, 1080, VideoPixelFormat.Yuv420P8, declared));
            Assert.Equal(primaries, resolved.Primaries);
            Assert.Equal(matrix, resolved.Matrix);
            Assert.Equal(TransferFunction.Gamma22, resolved.Transfer);
        }

        [Fact]
        public void CompleteAndUnsupportedDeclarationsAreNeverReplaced()
        {
            VideoColorInfo declared = new(ColorPrimaries.DciP3, TransferFunction.Gamma22, MatrixCoefficients.YCgCo, ColorRange.Full);
            Assert.Same(declared, PreviewColorPolicy.Resolve(new VideoFormat(96, 64, VideoPixelFormat.Yuv420P8, declared)));
            VideoColorInfo partial = declared with { Transfer = TransferFunction.Unspecified };
            VideoColorInfo resolved = PreviewColorPolicy.Resolve(new VideoFormat(96, 64, VideoPixelFormat.Yuv420P8, partial));
            Assert.Equal(declared.Primaries, resolved.Primaries);
            Assert.Equal(declared.Matrix, resolved.Matrix);
            Assert.Equal(declared.Range, resolved.Range);
        }

        [Theory]
        [InlineData(TransferFunction.Pq)]
        [InlineData(TransferFunction.Hlg)]
        public void HdrIsNeverInterpretedAsSdr(TransferFunction transfer)
        {
            VideoFormat format = new(96, 64, VideoPixelFormat.Yuv420P8, new(Transfer: transfer));
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => PreviewColorPolicy.Resolve(format));
            Assert.Contains("tone mapping", error.Message, StringComparison.Ordinal);
        }
    }
}
