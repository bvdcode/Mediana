// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1QuantizationMatrixTests
    {
        [Fact]
        public void EveryMatrixAndTransformExtentMatchesIndependentTableHashes()
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "quantization-matrices.sha256")));
            Assert.Equal("AV1QMAT1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(15, reader.ReadByte());
            Assert.Equal(2, reader.ReadByte());
            Assert.Equal(19, reader.ReadByte());
            for (int level = 0; level < 15; level++)
            {
                for (int plane = 0; plane < 2; plane++)
                {
                    for (int transform = 0; transform < 19; transform++)
                    {
                        Av1TransformSize size = Av1TransformSize.FromIndex(transform);
                        ReadOnlySpan<byte> weights = Av1QuantizationMatrices.Weights(level, plane, size);
                        Assert.Equal(size.CodedWidth * size.CodedHeight, weights.Length);
                        Assert.Equal(reader.ReadBytes(32), SHA256.HashData(weights));
                        if (plane == 1) { Assert.True(weights.SequenceEqual(Av1QuantizationMatrices.Weights(level, 2, size))); }
                    }
                }
            }
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }

        [Theory]
        [InlineData(false, 7, 117)]
        [InlineData(true, 14, 5)]
        public void MatrixSyntaxPreservesIndependentChromaLevelsAndFollowingBits(bool separateUv, int expectedV, int remaining)
        {
            Av1BitReader bits = new(new byte[] { 0x83, 0xF5 });
            Av1QuantizationMatrix matrix = Av1QuantizationMatrix.Read(ref bits, separateUv);
            Assert.Equal(new Av1QuantizationMatrix(true, 0, 7, (byte)expectedV), matrix);
            Assert.Equal(remaining, bits.Read(separateUv ? 3 : 7));
        }

        [Fact]
        public void DisabledMatrixSyntaxConsumesOnlyThePresenceFlag()
        {
            Av1BitReader bits = new(new byte[] { 0x55 });
            Assert.Equal(default, Av1QuantizationMatrix.Read(ref bits, true));
            Assert.Equal(0x55, bits.Read(7));
        }

        [Fact]
        public void TruncatedMatrixLevelsFailBeforeReadingTheFollowingHeader() =>
            Assert.Throws<InvalidDataException>(ReadTruncatedMatrix);

        [Theory]
        [InlineData(false, false, 0, 0)]
        [InlineData(true, true, 0, 0)]
        [InlineData(true, false, 9, 0)]
        [InlineData(true, false, 15, 0)]
        [InlineData(true, false, 0, 15)]
        public void LosslessIdentityAndFlatLevelsBypassMatrixWeights(bool enabled, bool lossless, int transform, byte level)
        {
            Av1QuantizationMatrix matrix = new(enabled, level, level, level);
            Assert.True(matrix.Weights(0, new(4, 4), transform, lossless).IsEmpty);
        }

        [Theory]
        [InlineData(571233, 11425, false, 0, 1)]
        [InlineData(571233, 11425, true, 0, -1)]
        [InlineData(571233, 11425, true, 2, 0)]
        [InlineData(1048575, 13825, false, 0, 32767)]
        [InlineData(1048575, 13825, true, 0, -32768)]
        [InlineData(37, 63, false, 2, 582)]
        [InlineData(37, 63, true, 2, -582)]
        public void DequantizationMasksTheWideMagnitudeBeforeDivisionAndClamping(int level, int multiplier, bool negative, int shift, int expected) =>
            Assert.Equal(expected, Av1Quantization.Dequantize(level, multiplier, negative, shift));

        [Theory]
        [InlineData("matrix00", 0, false)]
        [InlineData("matrix01", 1, false)]
        [InlineData("matrix02", 2, false)]
        [InlineData("matrix03", 3, false)]
        [InlineData("matrix04", 4, false)]
        [InlineData("matrix05", 5, false)]
        [InlineData("matrix06", 6, false)]
        [InlineData("matrix07", 7, false)]
        [InlineData("matrix08", 8, false)]
        [InlineData("matrix09", 9, false)]
        [InlineData("matrix10", 10, false)]
        [InlineData("matrix11", 11, false)]
        [InlineData("matrix12", 12, false)]
        [InlineData("matrix13", 13, false)]
        [InlineData("matrix14", 14, false)]
        [InlineData("matrixFlat", 15, false)]
        [InlineData("matrixOdd", 3, false)]
        [InlineData("matrixMono", 7, true)]
        [InlineData("matrixLossless", 0, false)]
        public void PublicDecoderMatchesIndependentResidualPicturesAtEveryMatrixLevel(string fixture, byte level, bool monochrome)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            Av1PictureSequenceTestInput.Verify(root, fixture, [0x81, 0, monochrome ? (byte)28 : (byte)12, 0]);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            {
                Av1FrameUnitReader units = new(File.ReadAllBytes(Path.Combine(root, fixture + ".obu")));
                int count = 0;
                while (units.TryRead(out ReadOnlySpan<byte> unit, default))
                {
                    using Av1Picture picture = decoder.Decode(unit);
                    Assert.Equal(new Av1QuantizationMatrix(true, level, level, level), picture.Header.Quantization.Matrix);
                    if (fixture == "matrixLossless") { Assert.True(picture.Header.Quantization.Lossless); }
                    count++;
                }
                Assert.Equal(6, count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void ReadTruncatedMatrix()
        {
            Av1BitReader bits = new(new byte[] { 0xFF });
            Av1QuantizationMatrix.Read(ref bits, false);
        }
    }
}
