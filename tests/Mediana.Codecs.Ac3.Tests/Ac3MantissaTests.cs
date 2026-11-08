// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Ac3;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3MantissaTests
    {
        [Fact]
        public void BoundedExponentScalingIsExactlyEquivalentToBinaryScaling()
        {
            ReadOnlySpan<double> mantissas = [-1, -0.7071067811865476, -1.0 / 32768, 0, 1.0 / 7, 10.0 / 11, 1];
            for (int exponent = 0; exponent <= 24; exponent++)
            {
                foreach (double mantissa in mantissas) { Assert.Equal(Math.ScaleB(mantissa, -exponent), Ac3MantissaReader.ApplyExponent(mantissa, exponent)); }
            }
        }
        [Fact]
        public void GroupedQuantizersPreserveTheirIndependentPendingCodes()
        {
            Ac3TestBitWriter writer = new(32);
            writer.Write(9 * 2 + 3, 5); writer.Write(25 * 4 + 5 * 3 + 1, 7); writer.Write(11 * 9 + 2, 7);
            Ac3BitReader bits = new(writer.Finish(true));
            Ac3MantissaReader reader = new();
            Assert.Equal(2.0 / 3, reader.Read(ref bits, 1));
            Assert.Equal(4.0 / 5, reader.Read(ref bits, 2));
            Assert.Equal(8.0 / 11, reader.Read(ref bits, 4));
            Assert.Equal(0, reader.Read(ref bits, 1));
            Assert.Equal(2.0 / 5, reader.Read(ref bits, 2));
            Assert.Equal(-6.0 / 11, reader.Read(ref bits, 4));
            Assert.Equal(-2.0 / 3, reader.Read(ref bits, 1));
            Assert.Equal(-2.0 / 5, reader.Read(ref bits, 2));
            Assert.Equal(19, bits.Position);
        }

        [Theory]
        [InlineData(3, 3)]
        [InlineData(5, 4)]
        [InlineData(6, 5)]
        [InlineData(7, 6)]
        [InlineData(8, 7)]
        [InlineData(9, 8)]
        [InlineData(10, 9)]
        [InlineData(11, 10)]
        [InlineData(12, 11)]
        [InlineData(13, 12)]
        [InlineData(14, 14)]
        [InlineData(15, 16)]
        public void DecodesSymmetricAndSignedQuantizersAtTheirBounds(int bap, int count)
        {
            int maximum = (1 << count) - 1;
            Ac3TestBitWriter writer = new(32);
            if (bap is 3 or 5)
            {
                int levels = maximum;
                writer.Write(0, count); writer.Write(levels / 2, count); writer.Write(levels - 1, count);
                Ac3BitReader bits = new(writer.Finish(true)); Ac3MantissaReader reader = new();
                Assert.Equal((1 - levels) / (double)levels, reader.Read(ref bits, bap));
                Assert.Equal(0, reader.Read(ref bits, bap)); Assert.Equal((levels - 1) / (double)levels, reader.Read(ref bits, bap));
            }
            else
            {
                writer.Write(1 << (count - 1), count); writer.Write(maximum, count); writer.Write(maximum / 2, count);
                Ac3BitReader bits = new(writer.Finish(true)); Ac3MantissaReader reader = new();
                Assert.Equal(-1, reader.Read(ref bits, bap));
                Assert.Equal(-Math.ScaleB(1, 1 - count), reader.Read(ref bits, bap));
                Assert.Equal(1 - Math.ScaleB(1, 1 - count), reader.Read(ref bits, bap));
            }
        }

        [Theory]
        [InlineData(1, 27, 5)]
        [InlineData(2, 125, 7)]
        [InlineData(3, 7, 3)]
        [InlineData(4, 121, 7)]
        [InlineData(5, 15, 4)]
        public void RejectsReservedMantissaCodes(int bap, int code, int count)
        {
            Ac3TestBitWriter writer = new(32); writer.Write(code, count); byte[] data = writer.Finish(true);
            Assert.Throws<InvalidDataException>(() => Decode(data, bap));
        }
        private static double Decode(byte[] data, int bap)
        {
            Ac3BitReader bits = new(data); Ac3MantissaReader reader = new(); return reader.Read(ref bits, bap);
        }
    }
}
