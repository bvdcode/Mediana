namespace Mediana.Tests
{
    internal class AacTestBitWriter
    {
        private readonly List<bool> bits = [];

        public AacTestBitWriter Write(int value, int width)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfNegative(width);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 24);
            if (value >= 1 << width)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            for (int bit = width - 1; bit >= 0; bit--)
            {
                bits.Add((value & (1 << bit)) != 0);
            }
            return this;
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[(bits.Count + 7) / 8];
            for (int index = 0; index < bits.Count; index++)
            {
                if (bits[index])
                {
                    result[index / 8] |= (byte)(1 << (7 - index % 8));
                }
            }
            return result;
        }
    }
}
