namespace Mediana.Containers.Mp4
{
    internal class Mp4BoxCollection
    {
        private readonly List<Mp4Box> boxes = [];

        public Mp4BoxCollection(ReadOnlyMemory<byte> data, Mp4ParseBudget budget)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                budget.TakeBox();
                ReadOnlySpan<byte> remaining = data.Span[offset..];
                uint shortSize = Mp4Binary.UInt32(remaining, 0);
                string type = Mp4Binary.FourCc(remaining, 4);
                int headerSize = shortSize == 1 ? 16 : 8;
                ulong size = shortSize switch
                {
                    0 => (ulong)remaining.Length,
                    1 => Mp4Binary.UInt64(remaining, 8),
                    _ => shortSize
                };
                if (type == "uuid")
                {
                    headerSize += 16;
                }
                if (size < (ulong)headerSize || size > (ulong)remaining.Length)
                {
                    throw new InvalidDataException($"Invalid MP4 '{type}' box size.");
                }
                int length = (int)size;
                boxes.Add(new Mp4Box(type, data.Slice(offset + headerSize, length - headerSize)));
                offset += length;
            }
        }

        public IReadOnlyList<Mp4Box> Boxes => boxes;

        public Mp4Box Required(string type) => Optional(type)
            ?? throw new InvalidDataException($"The required MP4 '{type}' box is missing.");

        public Mp4Box? Optional(string type)
        {
            Mp4Box? found = null;
            foreach (Mp4Box box in boxes)
            {
                if (box.Type == type)
                {
                    if (found is not null)
                    {
                        throw new InvalidDataException($"The MP4 '{type}' box is duplicated.");
                    }
                    found = box;
                }
            }
            return found;
        }
    }
}
