namespace Mediana.Codecs.Mp3
{
    internal ref struct Mp3BitReader
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly int end;

        public Mp3BitReader(ReadOnlySpan<byte> data, int start, int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            if ((long)start + length > (long)data.Length * 8 || length > int.MaxValue - start)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            this.data = data;
            Position = start;
            end = start + length;
        }

        public int Position { get; private set; }
        public readonly int Remaining => end - Position;

        public int Read(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 24);
            if (count > Remaining)
            {
                throw new InvalidDataException("The MP3 bit field exceeds its declared data boundary.");
            }
            int value = 0;
            while (count > 0)
            {
                int available = 8 - (Position & 7);
                int take = Math.Min(count, available);
                value = (value << take) | ((data[Position >> 3] >> (available - take)) & ((1 << take) - 1));
                Position += take;
                count -= take;
            }
            return value;
        }

        public void Skip(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > Remaining)
            {
                throw new InvalidDataException("The MP3 bit field exceeds its declared data boundary.");
            }
            Position += count;
        }
    }
}
