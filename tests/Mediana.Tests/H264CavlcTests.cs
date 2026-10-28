using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CavlcTests
    {
        [Theory]
        [InlineData(0, "1")]
        [InlineData(2, "11")]
        [InlineData(4, "1111")]
        [InlineData(8, "000011")]
        [InlineData(-1, "01")]
        public void ReadsEmptyBlocksAcrossCoefficientContexts(int context, string code)
        {
            H264BitReader bits = new(H264TestBits.Rbsp(code));
            int[] coefficients = new int[context == -1 ? 4 : 16];
            Assert.Equal(0, H264Cavlc.Read(ref bits, coefficients, context));
            Assert.All(coefficients, coefficient => Assert.Equal(0, coefficient));
            bits.ReadTrailingBits();
        }

        [Theory]
        [InlineData("01 0 1", 0, 1)]
        [InlineData("01 1 000000001", 15, -1)]
        [InlineData("000101 1 1", 0, 2)]
        [InlineData("000101 01 1", 0, -2)]
        [InlineData("000101 000000000000001 1111 1", 0, -16)]
        public void ReadsSignedLevelsAndZeroRuns(string code, int position, int expected)
        {
            H264BitReader bits = new(H264TestBits.Rbsp(code));
            int[] coefficients = new int[16];
            Assert.Equal(1, H264Cavlc.Read(ref bits, coefficients, 0));
            Assert.Equal(expected, coefficients[position]);
            Assert.Equal(1, coefficients.Count(value => value != 0));
            bits.ReadTrailingBits();
        }

        [Fact]
        public void CombinesReverseLevelsWithRuns()
        {
            H264BitReader bits = new(H264TestBits.Rbsp("001 01 101 01"));
            int[] coefficients = new int[16];
            Assert.Equal(2, H264Cavlc.Read(ref bits, coefficients, 0));
            Assert.Equal(-1, coefficients[1]);
            Assert.Equal(1, coefficients[3]);
            bits.ReadTrailingBits();
        }

        [Fact]
        public void ReadsChromaDcWithItsOwnCoefficientAndZeroTables()
        {
            H264BitReader bits = new(H264TestBits.Rbsp("001 01 01 0"));
            int[] coefficients = new int[4];
            Assert.Equal(2, H264Cavlc.Read(ref bits, coefficients, -1));
            Assert.Equal(new[] { -1, 0, 1, 0 }, coefficients);
            bits.ReadTrailingBits();
        }

        [Theory]
        [InlineData("000010", 8, 16)]
        [InlineData("01 0 000000001", 0, 15)]
        [InlineData("0000000000000000", 0, 16)]
        public void RejectsInvalidTokensOrRuns(string code, int context, int length)
        {
            Assert.Throws<InvalidDataException>(() => ReadBlock(code, context, length));
        }

        private static void ReadBlock(string code, int context, int length)
        {
            H264BitReader bits = new(H264TestBits.Rbsp(code));
            H264Cavlc.Read(ref bits, new int[length], context);
        }
    }
}
