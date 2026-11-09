// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CdfTests
    {
        [Theory]
        [InlineData(new ushort[] { })]
        [InlineData(new ushort[] { 32768, 0 })]
        [InlineData(new ushort[] { 20000, 10000, 32768, 0 })]
        [InlineData(new ushort[] { 16384, 32767, 0 })]
        [InlineData(new ushort[] { 16384, 32768, 33 })]
        [InlineData(new ushort[] { 40000, 32768, 0 })]
        public void RejectsInvalidCdf(ushort[] cdf)
        {
            Assert.Throws<ArgumentException>(() => Av1Cdf.Validate(cdf));
        }

        [Fact]
        public void RejectsLargerAlphabet()
        {
            ushort[] cdf = new ushort[18];
            cdf[^2] = Av1Cdf.Top;
            Assert.Throws<ArgumentException>(() => Av1Cdf.Validate(cdf));
        }

        [Fact]
        public void InvalidCdfDoesNotConsumeOrMutatePartition()
        {
            ushort[] invalid = [16384, 32767, 0];
            Av1SymbolReader reader = new([0x80]);
            int range = reader.Range;
            int value = reader.Value;
            try
            {
                reader.ReadSymbol(invalid);
                Assert.Fail("An invalid CDF was accepted.");
            }
            catch (ArgumentException)
            {
                Assert.Equal(range, reader.Range);
                Assert.Equal(value, reader.Value);
                Assert.Equal(1, reader.BitsConsumed);
                Assert.Equal(new ushort[] { 16384, 32767, 0 }, invalid);
            }
            reader.Finish();
        }
    }
}
