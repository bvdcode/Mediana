namespace Mediana.Containers.Mp4
{
    internal class Mp4Table
    {
        private readonly ReadOnlyMemory<byte> data;
        private readonly int stride;

        public Mp4Table(ReadOnlyMemory<byte> payload, int stride, byte version = 0)
        {
            Mp4Binary.FullBox(payload.Span, version);
            uint count = Mp4Binary.UInt32(payload.Span, 4);
            if (count > int.MaxValue || 8L + (long)count * stride != payload.Length)
            {
                throw new InvalidDataException("An MP4 table count does not match its box size.");
            }
            Count = (int)count;
            data = payload[8..];
            this.stride = stride;
        }

        public int Count { get; }
        public uint UInt32(int entry, int fieldOffset = 0) => Mp4Binary.UInt32(data.Span, EntryOffset(entry) + fieldOffset);
        public int Int32(int entry, int fieldOffset = 0) => Mp4Binary.Int32(data.Span, EntryOffset(entry) + fieldOffset);
        public ulong UInt64(int entry, int fieldOffset = 0) => Mp4Binary.UInt64(data.Span, EntryOffset(entry) + fieldOffset);

        private int EntryOffset(int entry)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(entry);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entry, Count);
            return entry * stride;
        }
    }
}
