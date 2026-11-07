// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcReferencePictureTests
    {
        [Fact]
        public void ExplicitReferencesRetainNegativePositiveOrderAndUnusedPictures()
        {
            BitReader reader = Bits(Ue(2) + Ue(2) + Ue(0) + "1" + Ue(2) + "0" + Ue(1) + "0" + Ue(2) + "1");
            ShortTermReferenceSet set = ShortTermReferenceSet.Read(reader, [], 0, false);
            Assert.Equal(new HevcShortTermReference[] { new(-1, true), new(-4, false), new(2, false), new(5, true) }, set.Pictures);
        }

        [Fact]
        public void PredictedReferencesPreserveUsageAcrossSortingAndDiscardZeroDelta()
        {
            ShortTermReferenceSet source = new([new(-1, true), new(-3, false), new(2, true)]);
            BitReader reader = Bits("10" + Ue(0) + "1" + "01" + "1" + "01");
            ShortTermReferenceSet result = ShortTermReferenceSet.Read(reader, [source], 1, false);
            Assert.Equal(new HevcShortTermReference[] { new(-2, false), new(1, false), new(3, true) }, result.Pictures);
        }

        [Fact]
        public void SlicePredictionCanSelectAnEarlierSequenceSet()
        {
            ShortTermReferenceSet first = new([new(-2, true)]);
            ShortTermReferenceSet last = new([new(-4, true)]);
            BitReader reader = Bits("1" + Ue(1) + "1" + Ue(0) + "1" + "00");
            ShortTermReferenceSet result = ShortTermReferenceSet.Read(reader, [first, last], 2, true);
            Assert.Equal(new HevcShortTermReference(-3, true), Assert.Single(result.Pictures));
        }

        [Fact]
        public void LongTermReferencesPreserveSequenceFlagsAndResetAccumulationForExplicitPictures()
        {
            ReferencePictureSyntax syntax = ReferencePictureSyntax.Read(Bits(Ue(0) + "1" + Ue(2) +
                Fixed(17, 8) + "1" + Fixed(193, 8) + "0"), 8);
            string header = Fixed(73, 8) + "0" + Ue(0) + Ue(0) + Ue(2) + Ue(2) +
                "01" + Ue(2) + "11" + Ue(3) + Fixed(50, 8) + "01" + Ue(7) + Fixed(10, 8) + "11" + Ue(1);
            HevcSliceReferences references = syntax.ReadSlice(Bits(header));
            Assert.Equal(73, references.PictureOrderCountLsb);
            Assert.Empty(references.ShortTerm.Pictures);
            Assert.Equal(new HevcLongTermReference[]
            {
                new(17, true, true, 2), new(193, false, true, 5), new(50, false, true, 7), new(10, true, true, 8)
            }, references.LongTerm);
            Assert.Equal(2, references.UsedCount);
        }

        [Fact]
        public void SelectingAbsentSequenceReferencesFailsBeforeSliceDecoding()
        {
            ReferencePictureSyntax syntax = ReferencePictureSyntax.Read(Bits(Ue(0) + "0"), 8);
            Assert.Throws<InvalidDataException>(() => syntax.ReadSlice(Bits(Fixed(0, 8) + "1")));
        }

        [Theory]
        [InlineData(17, 0)]
        [InlineData(12, 5)]
        public void InvalidReferenceCountCannotOverrunDecodedPictureBuffer(int negative, int positive)
        {
            Assert.Throws<InvalidDataException>(() => ShortTermReferenceSet.Read(Bits(Ue(negative) + Ue(positive)), [], 0, false));
        }

        private static BitReader Bits(string bits)
        {
            byte[] bytes = new byte[(bits.Length + 7) / 8];
            for (int index = 0; index < bits.Length; index++)
            {
                bytes[index / 8] |= (byte)((bits[index] - '0') << (7 - index % 8));
            }
            return new(new ArraySegment<byte>(bytes));
        }

        private static string Ue(int value)
        {
            string number = Convert.ToString(value + 1, 2);
            return new string('0', number.Length - 1) + number;
        }

        private static string Fixed(int value, int count) => Convert.ToString(value, 2).PadLeft(count, '0');
    }
}
