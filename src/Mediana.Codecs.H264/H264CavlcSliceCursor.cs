// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal struct H264CavlcSliceCursor
    {
        private readonly int firstMacroblock;
        private readonly int endMacroblock;
        private readonly bool hasSkipRuns;
        private readonly bool adaptiveFields;
        private int nextMacroblock;
        private int remainingSkip;
        private bool readSkipRun;

        public H264CavlcSliceCursor(H264PictureLayout layout, H264SliceKind kind, int firstMacroblock, int endMacroblock)
        {
            if (firstMacroblock < 0 || endMacroblock <= firstMacroblock || endMacroblock > layout.MacroblockCount)
            {
                throw new ArgumentOutOfRangeException(nameof(endMacroblock));
            }
            if (layout.MacroblockAdaptiveFrameField && ((firstMacroblock | endMacroblock) & 1) != 0)
            {
                throw new InvalidDataException("An MBAFF slice must contain complete macroblock pairs.");
            }
            hasSkipRuns = kind switch
            {
                H264SliceKind.I => false,
                H264SliceKind.P or H264SliceKind.B => true,
                _ => throw new NotSupportedException("CAVLC reconstruction supports I, P and B slices.")
            };
            this.firstMacroblock = firstMacroblock;
            this.endMacroblock = endMacroblock;
            adaptiveFields = layout.MacroblockAdaptiveFrameField;
            nextMacroblock = firstMacroblock;
            remainingSkip = 0;
            readSkipRun = hasSkipRuns;
        }

        public bool MoveNext(ref H264BitReader bits, H264MacroblockGrid grid, out int address, out bool skipped)
        {
            address = nextMacroblock;
            skipped = false;
            if (address == endMacroblock)
            {
                return false;
            }
            if (remainingSkip == 0)
            {
                if (!bits.MoreRbspData())
                {
                    throw new InvalidDataException("The H.264 slice does not cover its declared macroblock range.");
                }
                if (readSkipRun)
                {
                    remainingSkip = bits.ReadUe(endMacroblock - address);
                    readSkipRun = false;
                }
            }
            skipped = remainingSkip > 0;
            if (adaptiveFields && (address & 1) == 0)
            {
                bool field;
                if (remainingSkip >= 2)
                {
                    field = grid.InferFieldPair(address, firstMacroblock);
                }
                else
                {
                    field = bits.ReadFlag();
                }
                grid.SetFieldPair(address, field);
            }
            if (skipped)
            {
                remainingSkip--;
            }
            else
            {
                readSkipRun = hasSkipRuns;
            }
            nextMacroblock++;
            return true;
        }
    }
}
