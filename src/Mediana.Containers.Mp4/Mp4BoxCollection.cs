// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.IsoBmff;

namespace Mediana.Containers.Mp4
{
    internal class Mp4BoxCollection
    {
        private readonly List<Mp4Box> boxes = [];

        public Mp4BoxCollection(ReadOnlyMemory<byte> data, Mp4ParseBudget budget, bool allowTerminator = false)
        {
            IsoBmffBoxReader reader = new(data);
            while (!reader.Remaining.IsEmpty)
            {
                if (allowTerminator && reader.Remaining.Length == 4 && Mp4Binary.UInt32(reader.Remaining.Span, 0) == 0) { break; }
                budget.TakeBox();
                reader.TryRead(out IsoBmffBox box);
                boxes.Add(new(box.TypeName, box.Payload));
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
