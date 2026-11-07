// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcPredictionWeightTests
    {
        [Fact]
        public void MonochromeWeightsDoNotConsumeChromaFlagsOrDeltas()
        {
            BitReader reader = Bits(Ue(3) + "1" + Se(-7) + Se(127) + "10101010");
            HevcPredictionWeights weights = HevcPredictionWeights.Read(reader, false, 1, 0);
            Assert.Equal(new HevcPredictionWeight(1, 127, 8, 0, 8, 0), Assert.Single(weights.List0));
            Assert.Empty(weights.List1);
            Assert.Equal(170, reader.Read(8));
        }

        [Theory]
        [InlineData(-128, -512, 127)]
        [InlineData(127, 511, -128)]
        public void ChromaOffsetsClipAfterTheWeightCorrection(int deltaWeight, int offset, int expected)
        {
            string bits = Ue(0) + Se(0) + "01" + Se(deltaWeight) + Se(offset) + Se(deltaWeight) + Se(offset);
            HevcPredictionWeights weights = HevcPredictionWeights.Read(Bits(bits), true, 1, 0);
            HevcPredictionWeight actual = Assert.Single(weights.List0);
            Assert.Equal(expected, actual.CbOffset);
            Assert.Equal(expected, actual.CrOffset);
        }

        [Fact]
        public void ComponentFlagsCannotExceedTheCombinedListLimit()
        {
            string first = new('1', 8);
            string second = new('1', 1);
            string firstValues = string.Concat(Enumerable.Repeat(Se(0) + Se(0) + Se(0) + Se(0) + Se(0) + Se(0), 8));
            Assert.Throws<InvalidDataException>(() => HevcPredictionWeights.Read(Bits(Ue(0) + Se(0) + first + first + firstValues + second + second), true, 8, 1));
        }

        [Fact]
        public void InvalidChromaDenominatorIsRejectedBeforeReadingItsLists()
        {
            Assert.Throws<InvalidDataException>(() => HevcPredictionWeights.Read(Bits(Ue(7) + Se(1)), true, 1, 0));
        }

        private static BitReader Bits(string bits)
        {
            byte[] bytes = new byte[(bits.Length + 7) / 8];
            for (int i = 0; i < bits.Length; i++) { bytes[i / 8] |= (byte)((bits[i] - '0') << (7 - i % 8)); }
            return new(new ArraySegment<byte>(bytes));
        }
        private static string Se(int value) => Ue(value > 0 ? 2 * value - 1 : -2 * value);
        private static string Ue(int value)
        {
            string code = Convert.ToString(value + 1, 2);
            return new string('0', code.Length - 1) + code;
        }
    }
}
