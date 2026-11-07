// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static class TheoraFrameHeader
    {
        internal static bool Read(ref TheoraBitReader reader, scoped Span<int> qualities, out int qualityCount)
        {
            if (reader.Flag()) { throw new InvalidDataException("A Theora data packet has its header bit set."); }
            bool inter = reader.Flag(); qualities[0] = reader.Read(6); qualityCount = 1;
            if (reader.Flag())
            {
                qualities[1] = reader.Read(6); qualityCount = 2;
                if (reader.Flag()) { qualities[2] = reader.Read(6); qualityCount = 3; }
            }
            if (!inter && reader.Read(3) != 0) { throw new InvalidDataException("The Theora intra frame reserved bits are nonzero."); }
            return inter;
        }
    }
}
