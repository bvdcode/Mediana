// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void ReadBlockDeltas(int width, int height, bool skip)
        {
            if (!readDeltas) { return; }
            readDeltas = false;
            int side = sequence.Superblock128 ? 32 : 16;
            if (width == side && height == side && skip) { return; }
            int delta = ReadBlockDelta(Av1DefaultCdfs.DeltaQOffset);
            currentQuantizer = Math.Clamp(currentQuantizer + (delta << header.Deltas.QuantizerShift), 1, 255);
            if (!header.Deltas.FilterPresent) { return; }
            int count = header.Deltas.MultiFilter ? sequence.Monochrome ? 2 : 4 : 1;
            for (int index = 0; index < count; index++)
            {
                int offset = header.Deltas.MultiFilter ? Av1DefaultCdfs.DeltaLfMultiOffset + index * 5 : Av1DefaultCdfs.DeltaLfOffset;
                int difference = ReadBlockDelta(offset) << header.Deltas.FilterShift;
                filterDeltas[index] = (sbyte)Math.Clamp(filterDeltas[index] + difference, -63, 63);
            }
        }

        private int ReadBlockDelta(int offset)
        {
            int absolute = symbols.ReadSymbol(probabilities.Slice(offset, 5));
            if (absolute == 3)
            {
                int count = (int)symbols.ReadLiteral(3) + 1;
                absolute = (int)symbols.ReadLiteral(count) + (1 << count) + 1;
            }
            return absolute != 0 && symbols.ReadBoolean() ? -absolute : absolute;
        }

        private Av1BlockFilterDeltas BlockFilterDeltas()
        {
            if (!header.Deltas.FilterPresent) { return default; }
            if (header.Deltas.MultiFilter) { return filterDeltas; }
            Av1BlockFilterDeltas result = default;
            for (int index = 0; index < 4; index++) { result[index] = filterDeltas[0]; }
            return result;
        }
    }
}
