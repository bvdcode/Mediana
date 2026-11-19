// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.IO;
using Xunit;

namespace Mediana.Tests
{
    public class BoundedZLibDecoderTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(16383)]
        [InlineData(16384)]
        [InlineData(16385)]
        [InlineData(65536)]
        [InlineData(65537)]
        [InlineData(1048576)]
        public void ExactLimitAndSegmentBoundariesPreserveEveryByte(int length)
        {
            byte[] expected = new byte[length];
            new Random(917).NextBytes(expected);
            byte[] compressed = Compress(expected);
            Assert.Equal(expected, BoundedZLibDecoder.Decode(compressed, length));
            Assert.Equal(expected, BoundedZLibDecoder.Decode(compressed, length + 1));
            if (length != 0)
            {
                Assert.Throws<InvalidDataException>(() => BoundedZLibDecoder.Decode(compressed, length - 1));
            }
        }

        [Fact]
        public void MissingTrailerAndCorruptChecksumAreRejectedEvenAfterAllSamples()
        {
            byte[] compressed = Compress(new byte[65536]);
            for (int length = 0; length < compressed.Length; length++)
            {
                int prefix = length;
                Assert.Throws<InvalidDataException>(() => BoundedZLibDecoder.Decode(compressed.AsSpan(0, prefix), 65536));
            }
            compressed[^1] ^= 1;
            Assert.Throws<InvalidDataException>(() => BoundedZLibDecoder.Decode(compressed, 65536));
        }

        [Fact]
        public void OneMemberDoesNotConsumeTrailingPayload()
        {
            byte[] member = Convert.FromHexString("789CCB48CDC9C90700062C0215");
            Assert.Equal("hello"u8.ToArray(), BoundedZLibDecoder.Decode([.. member, 0xff, 0x00], 5));
        }

        [Fact]
        public void CancellationAndInvalidBudgetAreCheckedBeforeDecoding()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoundedZLibDecoder.Decode([], -1));
            Assert.ThrowsAny<OperationCanceledException>(() => BoundedZLibDecoder.Decode([], 0, new(true)));
            Assert.Throws<InvalidDataException>(() => BoundedZLibDecoder.Decode([], 0));
        }

        private static byte[] Compress(byte[] input)
        {
            using MemoryStream output = new();
            using (ZLibStream compressor = new(output, CompressionLevel.Optimal, true)) { compressor.Write(input); }
            return output.ToArray();
        }
    }
}
