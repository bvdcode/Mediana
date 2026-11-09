// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SymbolReaderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DecodesIndependentArithmeticStatesAndCdfAdaptation(bool adaptive)
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "entropy.bin.gz"));
            using GZipStream compressed = new(file, CompressionMode.Decompress);
            using BinaryReader input = new(compressed);
            Assert.Equal("AV1ENT01", System.Text.Encoding.ASCII.GetString(input.ReadBytes(8)));
            uint cases = input.ReadUInt32();
            int selectedCases = 0;
            long selectedEvents = 0;
            for (uint index = 0; index < cases; index++)
            {
                int symbols = input.ReadByte();
                int shape = input.ReadByte();
                bool updates = input.ReadBoolean();
                int initialCount = input.ReadByte();
                uint events = input.ReadUInt32();
                int encodedLength = checked((int)input.ReadUInt32());
                ushort[] cdf = ReadCdf(input, symbols);
                Assert.Equal(initialCount, cdf[symbols]);
                Assert.InRange(shape, 0, 3);
                byte[] encoded = input.ReadBytes(encodedLength);
                Assert.Equal(encodedLength, encoded.Length);
                Av1SymbolReader reader = new(encoded, updates);
                bool selected = updates == adaptive;
                if (selected)
                {
                    selectedCases++;
                    selectedEvents += events;
                }
                for (uint ordinal = 0; ordinal < events; ordinal++)
                {
                    int operation = input.ReadByte();
                    int bits = input.ReadByte();
                    uint expected = input.ReadUInt32();
                    int range = input.ReadUInt16();
                    int value = input.ReadUInt16();
                    long consumed = input.ReadUInt32();
                    ushort[] expectedCdf = ReadCdf(input, symbols);
                    if (!selected)
                    {
                        continue;
                    }
                    uint actual = operation switch
                    {
                        0 => (uint)reader.ReadSymbol(cdf),
                        1 => reader.ReadBoolean() ? 1U : 0U,
                        2 => reader.ReadLiteral(bits),
                        _ => throw new InvalidDataException("An entropy fixture operation is invalid.")
                    };
                    Assert.True(actual == expected, $"Case {index}, operation {ordinal}: expected {expected}, actual {actual}.");
                    Assert.Equal(range, reader.Range);
                    Assert.Equal(value, reader.Value);
                    Assert.Equal(consumed, reader.BitsConsumed);
                    Assert.Equal(expectedCdf, cdf);
                }
                if (selected)
                {
                    reader.Finish();
                }
            }
            Assert.Equal(600U, cases);
            Assert.Equal(300, selectedCases);
            Assert.Equal(28800, selectedEvents);
            Assert.Equal(-1, compressed.ReadByte());
        }

        [Theory]
        [InlineData(new byte[] { 0x80 })]
        [InlineData(new byte[] { 0x80, 0x00 })]
        [InlineData(new byte[] { 0x80, 0x00, 0x00 })]
        public void AcceptsOneTerminationBitAndOnlyZeroPadding(byte[] encoded)
        {
            Av1SymbolReader reader = new(encoded);
            Assert.Equal(1, reader.BitsConsumed);
            reader.Finish();
        }

        [Theory]
        [InlineData(new byte[] { 0x00 })]
        [InlineData(new byte[] { 0xC0 })]
        [InlineData(new byte[] { 0x80, 0x01 })]
        [InlineData(new byte[] { 0x80, 0x00, 0x01 })]
        public void RejectsInvalidPhysicalTermination(byte[] encoded)
        {
            Assert.Throws<InvalidDataException>(() => Finish(encoded));
        }

        [Fact]
        public void RejectsEmptyPartition()
        {
            Assert.Throws<InvalidDataException>(() => Finish([]));
        }

        [Fact]
        public void RejectsExcessVirtualZeroPadding()
        {
            Assert.Throws<InvalidDataException>(ReadPastVirtualPadding);
        }

        [Fact]
        public void FinishedPartitionCannotBeReused()
        {
            Assert.Throws<InvalidOperationException>(ReadAfterFinish);
            Assert.Throws<InvalidOperationException>(FinishTwice);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(33)]
        public void RejectsInvalidLiteralWidth(int width)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadLiteral(width));
        }

        [Fact]
        public void LiteralWithNoBitsDoesNotChangeArithmeticState()
        {
            Av1SymbolReader reader = new([0x80]);
            int range = reader.Range;
            int value = reader.Value;
            Assert.Equal(0U, reader.ReadLiteral(0));
            Assert.Equal(range, reader.Range);
            Assert.Equal(value, reader.Value);
            Assert.Equal(1, reader.BitsConsumed);
            reader.Finish();
        }

        private static ushort[] ReadCdf(BinaryReader input, int symbols)
        {
            ushort[] result = new ushort[symbols + 1];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = input.ReadUInt16();
            }
            return result;
        }

        private static void Finish(byte[] encoded)
        {
            Av1SymbolReader reader = new(encoded);
            reader.Finish();
        }

        private static void ReadPastVirtualPadding()
        {
            Av1SymbolReader reader = new([0x80]);
            for (int index = 0; index < 64; index++)
            {
                reader.ReadBoolean();
            }
            reader.Finish();
        }

        private static void ReadAfterFinish()
        {
            Av1SymbolReader reader = new([0x80]);
            reader.Finish();
            reader.ReadBoolean();
        }

        private static void FinishTwice()
        {
            Av1SymbolReader reader = new([0x80]);
            reader.Finish();
            reader.Finish();
        }

        private static void ReadLiteral(int width)
        {
            Av1SymbolReader reader = new([0x80]);
            reader.ReadLiteral(width);
        }
    }
}
