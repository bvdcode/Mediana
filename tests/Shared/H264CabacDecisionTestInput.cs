// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static partial class H264CabacDecisionTestInput
    {
        public static int Verify()
        {
            int cases = 0;
            int shifts = 0;
            int normalizedMps = 0;
            int unnormalizedMps = 0;
            foreach (int initialRange in new[] { 256, 320, 384, 448, 510 })
            {
                for (byte state = 0; state < 64; state++)
                {
                    for (int symbol = 0; symbol < 2; symbol++)
                    {
                        for (int initialOffset = 0; initialOffset < initialRange; initialOffset++)
                        {
                            byte[] payload = [(byte)(initialOffset >> 1), (byte)(((initialOffset & 1) << 7) | 0x55), 0x55, 0xaa];
                            H264BitReader bits = new(payload);
                            H264CabacDecoder decoder = new(ref bits);
                            for (int count = 0; count < (510 - initialRange) / 2; count++)
                            {
                                if (decoder.ReadTermination(ref bits))
                                {
                                    throw new InvalidDataException("The reference interval terminated before its selected range.");
                                }
                            }
                            H264BitReader expectedBits = bits;
                            int lpsRange = Ranges[state * 4 + ((initialRange >> 6) & 3)];
                            int expectedRange = initialRange - lpsRange;
                            int expectedOffset = initialOffset;
                            bool leastProbable = initialOffset >= expectedRange;
                            bool expectedSymbol = symbol != 0;
                            int expectedState;
                            bool expectedMps = symbol != 0;
                            if (leastProbable)
                            {
                                expectedOffset -= expectedRange;
                                expectedRange = lpsRange;
                                expectedSymbol = !expectedSymbol;
                                expectedState = LpsTransitions[state];
                                expectedMps = state == 0 ? !expectedMps : expectedMps;
                            }
                            else
                            {
                                expectedState = MpsTransitions[state];
                                if (expectedRange < 256) { normalizedMps++; }
                                else { unnormalizedMps++; }
                            }
                            int countShifts = 0;
                            while (expectedRange < 256)
                            {
                                expectedRange *= 2;
                                expectedOffset = expectedOffset * 2 + expectedBits.ReadBit();
                                countShifts++;
                            }
                            shifts |= 1 << countShifts;
                            H264CabacContext context = new(state, symbol != 0);
                            if (decoder.ReadBin(ref bits, ref context) != expectedSymbol || decoder.Range != expectedRange
                                || decoder.Offset != expectedOffset || context.State != expectedState
                                || context.MostProbableSymbol != expectedMps || bits.BitPosition != expectedBits.BitPosition
                                || bits.ByteAligned != expectedBits.ByteAligned)
                            {
                                throw new InvalidDataException("CABAC decision differs from normative probability, transition or arithmetic tables.");
                            }
                            cases++;
                        }
                    }
                }
            }
            if (cases != 245504 || shifts != 255 || normalizedMps == 0 || unnormalizedMps == 0)
            {
                throw new InvalidDataException("The CABAC decision corpus missed an offset, normalization shift or MPS path.");
            }
            return cases;
        }
    }
}
