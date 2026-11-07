// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static partial class TheoraFrameSyntax
    {
        internal static bool Read(ref TheoraBitReader reader, TheoraLayout layout, TheoraSetup setup,
            TheoraFrameState state, scoped Span<int> qualities, out int qualityCount, CancellationToken token)
        {
            state.Clear();
            if (reader.Flag()) { throw new InvalidDataException("A Theora data packet has its header bit set."); }
            bool inter = reader.Flag(); qualities[0] = reader.Read(6); qualityCount = 1;
            if (reader.Flag())
            {
                qualities[1] = reader.Read(6); qualityCount = 2;
                if (reader.Flag()) { qualities[2] = reader.Read(6); qualityCount = 3; }
            }
            if (!inter && reader.Read(3) != 0) { throw new InvalidDataException("The Theora intra frame reserved bits are nonzero."); }
            ReadCoded(ref reader, layout, state, inter);
            for (int block = 0; block < layout.BlockCount; block++)
            { if (state.Coded[block] != 0) { state.CodedBlocks[state.CodedCount++] = block; } }
            ReadModes(ref reader, layout, state, inter);
            if (inter) { ReadMotion(ref reader, layout, state); }
            ReadQuality(ref reader, state, qualityCount);
            ReadCoefficients(ref reader, layout, setup, state, token);
            return inter;
        }

        private static void ReadCoded(ref TheoraBitReader reader, TheoraLayout layout, TheoraFrameState state, bool inter)
        {
            if (!inter) { state.Coded.Fill(1); return; }
            TheoraRunDecoder.Read(ref reader, state.PartialSupers, true);
            int remaining = 0, partialBlocks = 0;
            for (int super = 0; super < layout.SuperCount; super++)
            {
                if (state.PartialSupers[super] == 0) { remaining++; }
                else { partialBlocks += layout.SuperStarts[super + 1] - layout.SuperStarts[super]; }
            }
            TheoraRunDecoder.Read(ref reader, state.Bits[..remaining], true);
            int index = 0;
            for (int super = 0; super < layout.SuperCount; super++)
            { if (state.PartialSupers[super] == 0) { state.FullSupers[super] = state.Bits[index++]; } }
            TheoraRunDecoder.Read(ref reader, state.Bits[..partialBlocks], false); index = 0;
            for (int super = 0; super < layout.SuperCount; super++)
            {
                for (int block = layout.SuperStarts[super]; block < layout.SuperStarts[super + 1]; block++)
                { state.Coded[block] = state.PartialSupers[super] == 0 ? state.FullSupers[super] : state.Bits[index++]; }
            }
        }

        private static void ReadModes(ref TheoraBitReader reader, TheoraLayout layout, TheoraFrameState state, bool inter)
        {
            if (!inter) { state.Modes.Fill(1); return; }
            int scheme = reader.Read(3); Span<byte> alphabet = stackalloc byte[8];
            if (scheme == 0)
            {
                int seen = 0;
                for (int mode = 0; mode < 8; mode++)
                {
                    int index = reader.Read(3);
                    if ((seen & (1 << index)) != 0) { throw new InvalidDataException("The Theora mode alphabet is not a permutation."); }
                    seen |= 1 << index; alphabet[index] = (byte)mode;
                }
            }
            else if (scheme != 7)
            {
                ReadOnlySpan<byte> modes = [3,4,2,0,1,5,6,7, 3,4,0,2,1,5,6,7, 3,2,4,0,1,5,6,7,
                    3,2,0,4,1,5,6,7, 0,3,4,2,1,5,6,7, 0,5,3,4,2,1,6,7];
                modes.Slice((scheme - 1) * 8, 8).CopyTo(alphabet);
            }
            foreach (int macro in layout.MacroOrder)
            {
                int x = macro % (layout.Info.CodedWidth / 16) * 2, y = macro / (layout.Info.CodedWidth / 16) * 2;
                if ((state.Coded[layout.Block(0, x, y)] | state.Coded[layout.Block(0, x + 1, y)] |
                    state.Coded[layout.Block(0, x, y + 1)] | state.Coded[layout.Block(0, x + 1, y + 1)]) == 0) { continue; }
                if (scheme == 7) { state.Modes[macro] = (byte)reader.Read(3); }
                else
                {
                    int index = 0; while (index < 7 && reader.Flag()) { index++; }
                    state.Modes[macro] = alphabet[index];
                }
            }
        }

        private static void ReadQuality(ref TheoraBitReader reader, TheoraFrameState state, int count)
        {
            for (int quality = 0; quality < count - 1; quality++)
            {
                int remaining = 0;
                for (int i = 0; i < state.CodedCount; i++)
                { if (state.QualityIndices[state.CodedBlocks[i]] == quality) { remaining++; } }
                TheoraRunDecoder.Read(ref reader, state.Bits[..remaining], true); int index = 0;
                for (int i = 0; i < state.CodedCount; i++)
                {
                    int block = state.CodedBlocks[i];
                    if (state.QualityIndices[block] == quality) { state.QualityIndices[block] += state.Bits[index++]; }
                }
            }
        }
    }
}
