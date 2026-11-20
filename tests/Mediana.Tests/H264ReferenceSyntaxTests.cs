// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264ReferenceSyntaxTests
    {
        [Fact]
        public void ReadsAllReferenceListModificationKinds()
        {
            H264BitReader bits = new(H264TestBits.Rbsp("1 1 1 010 011 011 00100 00100"));
            IReadOnlyList<H264ReferenceModification> result = H264ReferenceSyntax.ReadModifications(ref bits, 3, 16);
            bits.ReadTrailingBits();
            Assert.Equal(new[] { new H264ReferenceModification(0, 0), new H264ReferenceModification(1, 2),
                new H264ReferenceModification(2, 3) }, result);
        }

        [Fact]
        public void ReadsAllSixMemoryControlOperations()
        {
            byte[] payload = H264TestBits.Rbsp("010 1 011 010 00100 011 00100 00101 00101 00110 00111 011 1");
            H264BitReader bits = new(payload);
            IReadOnlyList<H264MemoryOperation> result = H264ReferenceSyntax.ReadMarking(ref bits, 16, 6);
            bits.ReadTrailingBits();
            Assert.Equal(new[] { new H264MemoryOperation(1), new H264MemoryOperation(2, LongTermPictureNumber: 1),
                new H264MemoryOperation(3, DifferenceMinusOne: 2, LongTermFrameIndex: 3),
                new H264MemoryOperation(4, MaxLongTermFrameIndexPlusOne: 4), new H264MemoryOperation(5),
                new H264MemoryOperation(6, LongTermFrameIndex: 2) }, result);
            Assert.Throws<NotSupportedException>(() => ReadMarking(payload, 2));
        }

        [Fact]
        public void RejectsInvalidAndUnterminatedMemoryCommands()
        {
            Assert.Throws<InvalidDataException>(() => ReadMarking(H264TestBits.Rbsp("0001000"), 16));
            Assert.Throws<InvalidDataException>(() => ReadMarking([0], 16));
        }

        [Theory]
        [InlineData(false, 15)]
        [InlineData(true, 31)]
        public void FrameAndFieldReferenceNumbersHaveDifferentRanges(bool fieldPicture, int maximum)
        {
            string value = H264TestBits.Ue(maximum);
            H264BitReader modifications = new(H264TestBits.Rbsp("1 1 " + value + " 011 " + value + " 00100"));
            IReadOnlyList<H264ReferenceModification> list = H264ReferenceSyntax.ReadModifications(ref modifications,
                2, maximum + 1, fieldPicture);
            modifications.ReadTrailingBits();
            Assert.Equal(new[] { new H264ReferenceModification(0, maximum), new H264ReferenceModification(2, maximum) }, list);
            H264BitReader marking = new(H264TestBits.Rbsp("010 " + value + " 011 " + value + " 1"));
            IReadOnlyList<H264MemoryOperation> operations = H264ReferenceSyntax.ReadMarking(ref marking,
                maximum + 1, 2, fieldPicture);
            marking.ReadTrailingBits();
            Assert.Equal(new[] { new H264MemoryOperation(1, DifferenceMinusOne: maximum),
                new H264MemoryOperation(2, LongTermPictureNumber: maximum) }, operations);
            byte[] invalidModification = H264TestBits.Rbsp("1 011 " + H264TestBits.Ue(maximum + 1));
            Assert.Throws<InvalidDataException>(() => ReadFieldModifications(invalidModification, maximum + 1, fieldPicture));
            byte[] invalidMarking = H264TestBits.Rbsp("011 " + H264TestBits.Ue(maximum + 1));
            Assert.Throws<InvalidDataException>(() => ReadFieldMarking(invalidMarking, maximum + 1, fieldPicture));
        }

        private static void ReadFieldModifications(byte[] payload, int maximumPictureNumber, bool fieldPicture)
        {
            H264BitReader bits = new(payload);
            H264ReferenceSyntax.ReadModifications(ref bits, 1, maximumPictureNumber, fieldPicture);
        }

        private static void ReadFieldMarking(byte[] payload, int maximumPictureNumber, bool fieldPicture)
        {
            H264BitReader bits = new(payload);
            H264ReferenceSyntax.ReadMarking(ref bits, maximumPictureNumber, 1, fieldPicture);
        }

        private static void ReadMarking(byte[] payload, int limit)
        {
            H264BitReader bits = new(payload);
            H264ReferenceSyntax.ReadMarking(ref bits, 16, limit);
        }
    }
}
