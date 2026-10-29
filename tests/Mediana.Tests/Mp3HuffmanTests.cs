using System.Globalization;
using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3HuffmanTests
    {
        [Fact]
        public void EveryCodewordAlignmentSignAndEscapeMatchesNormativeTuples()
        {
            Mp3HuffmanTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "huffman-codes.tsv"));
        }

        [Fact]
        public void TruncatedPairsCannotConsumeBitsFromTheFollowingGranule()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "huffman-codes.tsv");
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                string[] fields = line.Split('\t');
                int table = int.Parse(fields[0], CultureInfo.InvariantCulture);
                if (table >= 32)
                {
                    continue;
                }
                int packed = int.Parse(fields[1], CultureInfo.InvariantCulture);
                int[] magnitudes = [packed >> 4, packed & 15];
                string word = fields[3];
                foreach (int magnitude in magnitudes)
                {
                    if (magnitude == 15 && table >= 16)
                    {
                        word += table switch { 16 => "0", 24 => "0000", _ => throw new InvalidOperationException() };
                    }
                    if (magnitude != 0)
                    {
                        word += "1";
                    }
                }
                byte[] data = Mp3HuffmanTestInput.Pack("111" + word + "11111111");
                for (int available = 0; available < word.Length; available++)
                {
                    Assert.Throws<InvalidDataException>(() => ReadPair(data, table, available));
                }
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(14)]
        [InlineData(32)]
        [InlineData(33)]
        public void ReservedAndInvalidPairCodebooksAreRejected(int table)
        {
            Assert.Throws<InvalidDataException>(() => ReadPair([0, 0], table, 8));
        }

        [Fact]
        public void QuadCodebookAndOutputCapacityAreValidated()
        {
            Assert.Throws<InvalidDataException>(() => ReadQuad(-1, 4));
            Assert.Throws<InvalidDataException>(() => ReadQuad(2, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadQuad(0, 3));
        }

        private static void ReadPair(byte[] data, int table, int available)
        {
            Mp3BitReader bits = new(data, 3, available);
            Mp3Huffman.ReadPair(ref bits, table);
        }

        private static void ReadQuad(int table, int capacity)
        {
            Mp3BitReader bits = new([0xff], 0, 8);
            Mp3Huffman.TryReadQuad(ref bits, table, new int[capacity]);
        }
    }
}
