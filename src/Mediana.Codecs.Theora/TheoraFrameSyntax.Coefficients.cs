// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static partial class TheoraFrameSyntax
    {
        private static void ReadCoefficients(ref TheoraBitReader reader, TheoraLayout layout, TheoraSetup setup,
            TheoraFrameState state, CancellationToken token)
        {
            int eobs = 0, luma = 0, chroma = 0, active = state.CodedCount;
            for (int ti = 0; ti < 64; ti++)
            {
                token.ThrowIfCancellationRequested();
                if (ti is 0 or 1) { luma = reader.Read(4); chroma = reader.Read(4); }
                int group = ti switch { 0 => 0, <= 5 => 1, <= 14 => 2, <= 27 => 3, _ => 4 };
                for (int i = 0; i < state.CodedCount; i++)
                {
                    int block = state.CodedBlocks[i];
                    if (state.TokenIndices[block] != ti) { continue; }
                    state.CoefficientCounts[block] = (byte)ti;
                    if (eobs == 0)
                    {
                        int symbol = setup.Huffman[group * 16 + (block < layout.LumaBlocks ? luma : chroma)].Decode(ref reader);
                        if (symbol < 7)
                        {
                            eobs = symbol switch
                            { 0 => 1, 1 => 2, 2 => 3, 3 => 4 + reader.Read(2), 4 => 8 + reader.Read(3), 5 => 16 + reader.Read(4), 6 => reader.Read(12), _ => throw new InvalidDataException() };
                            if (symbol == 6 && eobs == 0) { eobs = active; }
                            if (eobs > active) { throw new InvalidDataException("A Theora EOB run exceeds its remaining coded blocks."); }
                        }
                        else
                        {
                            ReadCoefficient(ref reader, symbol, block, ti, state);
                            if (state.TokenIndices[block] == 64) { active--; }
                            continue;
                        }
                    }
                    eobs--; active--; state.TokenIndices[block] = 64;
                }
            }
            if (eobs != 0 || active != 0) { throw new InvalidDataException("The Theora coefficients do not terminate within their frame."); }
        }

        private static void ReadCoefficient(ref TheoraBitReader reader, int symbol, int block, int ti, TheoraFrameState state)
        {
            int zeros = 0, magnitude = 0; bool negative = false;
            switch (symbol)
            {
                case 7: zeros = reader.Read(3) + 1; break;
                case 8: zeros = reader.Read(6) + 1; break;
                case 9: magnitude = 1; break;
                case 10: magnitude = 1; negative = true; break;
                case 11: magnitude = 2; break;
                case 12: magnitude = 2; negative = true; break;
                case >= 13 and <= 16: magnitude = symbol - 10; negative = reader.Flag(); break;
                case >= 17 and <= 22:
                    negative = reader.Flag();
                    (int start, int bits) = symbol switch
                    { 17 => (7,1), 18 => (9,2), 19 => (13,3), 20 => (21,4), 21 => (37,5), 22 => (69,9), _ => throw new InvalidDataException() };
                    magnitude = start + reader.Read(bits); break;
                case >= 23 and <= 27: zeros = symbol - 22; negative = reader.Flag(); magnitude = 1; break;
                case 28: negative = reader.Flag(); zeros = 6 + reader.Read(2); magnitude = 1; break;
                case 29: negative = reader.Flag(); zeros = 10 + reader.Read(3); magnitude = 1; break;
                case 30: zeros = 1; negative = reader.Flag(); magnitude = 2 + reader.Read(1); break;
                case 31: negative = reader.Flag(); magnitude = 2 + reader.Read(1); zeros = 2 + reader.Read(1); break;
                default: throw new InvalidDataException("Invalid Theora coefficient token.");
            }
            int end = ti + zeros + (magnitude == 0 ? 0 : 1);
            if (end > 64) { throw new InvalidDataException("A Theora coefficient run exceeds its 8 by 8 block."); }
            if (magnitude != 0)
            {
                state.Coefficients[block * 64 + ti + zeros] = (short)(negative ? -magnitude : magnitude);
                state.CoefficientCounts[block] = (byte)end;
            }
            state.TokenIndices[block] = (byte)end;
        }
    }
}
