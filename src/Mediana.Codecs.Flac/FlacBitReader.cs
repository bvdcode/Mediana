using System.Numerics;

namespace Mediana.Codecs.Flac
{
    internal ref struct FlacBitReader(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> bytes = data;

        public int Position { get; private set; } = position;

        public bool TryRead(int count, out ulong value)
        {
            value = 0;
            if ((long)Position + count > (long)bytes.Length * 8)
            {
                return false;
            }
            int remaining = count;
            while (remaining > 0)
            {
                int available = 8 - (Position & 7);
                int take = Math.Min(remaining, available);
                value = (value << take) | (uint)((bytes[Position >> 3] >> (available - take)) & ((1 << take) - 1));
                Position += take;
                remaining -= take;
            }
            return true;
        }

        public bool TryReadSigned(int count, out long value)
        {
            bool available = TryRead(count, out ulong bits);
            value = count == 0 ? 0 : (long)(bits << (64 - count)) >> (64 - count);
            return available;
        }

        public bool TryReadUnary(ref uint zeros, uint maximum, CancellationToken cancellationToken)
        {
            while ((long)Position < (long)bytes.Length * 8)
            {
                if ((Position & 8191) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                int available = 8 - (Position & 7);
                uint bits = (uint)(bytes[Position >> 3] & ((1 << available) - 1));
                int leading = Math.Min(available, BitOperations.LeadingZeroCount(bits) - (32 - available));
                if (zeros > maximum || (uint)leading > maximum - zeros)
                {
                    throw new InvalidDataException("A FLAC unary code exceeds its permitted range.");
                }
                zeros += (uint)leading;
                Position += leading;
                if (leading < available)
                {
                    Position++;
                    return true;
                }
            }
            return false;
        }
    }
}
