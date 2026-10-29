namespace Mediana.Codecs.Aac
{
    internal class AacSpectralCodebook
    {
        private static readonly AacSpectralCodebook SignedQuads = new(4, 3, 1, true);
        private static readonly AacSpectralCodebook UnsignedQuads = new(4, 3, 0, false);
        private static readonly AacSpectralCodebook SignedPairs = new(2, 9, 4, true);
        private static readonly AacSpectralCodebook SmallPairs = new(2, 8, 0, false);
        private static readonly AacSpectralCodebook LargePairs = new(2, 13, 0, false);
        private static readonly AacSpectralCodebook EscapePairs = new(2, 17, 0, false);
        private readonly uint[] tuples;

        private AacSpectralCodebook(int dimensions, int radix, int offset, bool signed)
        {
            Dimensions = dimensions;
            Signed = signed;
            int count = 1;
            for (int index = 0; index < dimensions; index++)
            {
                count *= radix;
            }
            tuples = new uint[count];
            for (int symbol = 0; symbol < count; symbol++)
            {
                int value = symbol;
                uint packed = 0;
                for (int index = dimensions - 1; index >= 0; index--)
                {
                    packed |= (uint)unchecked((byte)(value % radix - offset)) << (index * 8);
                    value /= radix;
                }
                tuples[symbol] = packed;
            }
        }

        public int Dimensions { get; }
        public bool Signed { get; }

        public void Read(int symbol, Span<int> values)
        {
            uint packed = tuples[symbol];
            for (int index = 0; index < Dimensions; index++)
            {
                values[index] = unchecked((sbyte)(packed >> (index * 8)));
            }
        }

        public static AacSpectralCodebook Get(int codebook) => codebook switch
        {
            1 or 2 => SignedQuads,
            3 or 4 => UnsignedQuads,
            5 or 6 => SignedPairs,
            7 or 8 => SmallPairs,
            9 or 10 => LargePairs,
            11 => EscapePairs,
            _ => throw new ArgumentOutOfRangeException(nameof(codebook))
        };
    }
}
