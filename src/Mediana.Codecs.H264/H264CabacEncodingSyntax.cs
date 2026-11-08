// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264CabacEncodingSyntax(H264CabacEncoder encoder, H264CabacMacroblockState state,
        H264Picture target, H264SliceKind kind, int quantizer) : IH264EncodingSyntax
    {
        private readonly H264CabacContext[] contexts = kind switch
        {
            H264SliceKind.I => H264CabacIntraContexts.Create(quantizer),
            H264SliceKind.P => H264CabacPredictiveContexts.Create(quantizer, 0),
            _ => throw new NotSupportedException("CABAC encoding supports I and P slices.")
        };
        private int previousDelta;

        public void WriteBin(bool value, int context)
        {
            int index = kind switch
            {
                H264SliceKind.I => H264CabacIntraContexts.GetIndex(context),
                H264SliceKind.P => H264CabacPredictiveContexts.GetIndex(context),
                _ => throw new NotSupportedException("CABAC encoding supports I and P slices.")
            };
            encoder.WriteBin(value, ref contexts[index]);
        }

        public void WriteIntraType(int address)
        {
            state.Types[address] = 0;
            WriteBin(false, 3 + state.TypeCondition(state.Left(address)) + state.TypeCondition(state.Top(address)));
        }

        public void WriteIntraMode(int mode, int predicted)
        {
            WriteBin(mode == predicted, 68);
            if (mode == predicted) { return; }
            int remaining = mode < predicted ? mode : mode - 1;
            for (int bit = 0; bit < 3; bit++) { WriteBin((remaining & (1 << bit)) != 0, 69); }
        }

        public void WriteChromaMode(int address)
        {
            WriteBin(false, 64 + state.ChromaModeCondition(state.Left(address)) + state.ChromaModeCondition(state.Top(address)));
        }

        public void WriteSkip(int address, bool skipped)
        {
            int context = state.SkipCondition(state.Left(address)) + state.SkipCondition(state.Top(address));
            WriteBin(skipped, 11 + context);
            state.Types[address] = skipped ? H264CabacMacroblockState.Skipped : H264CabacMacroblockState.Inter;
            if (skipped) { previousDelta = 0; }
        }

        public void WritePredictiveType()
        {
            WriteBin(false, 14); WriteBin(false, 15); WriteBin(false, 16);
        }

        public void WritePattern(int address, int pattern)
        {
            int written = 0;
            for (int block = 0; block < 4; block++)
            {
                int context = 73 + state.LumaPatternCondition(address, block, true, written) +
                    2 * state.LumaPatternCondition(address, block, false, written);
                bool value = (pattern & (1 << block)) != 0;
                WriteBin(value, context);
                if (value) { written |= 1 << block; }
            }
            int chroma = pattern >> 4;
            WriteBin(chroma != 0, 77 + state.ChromaPatternCondition(state.Left(address), 0) +
                2 * state.ChromaPatternCondition(state.Top(address), 0));
            if (chroma != 0)
            {
                WriteBin(chroma == 2, 81 + state.ChromaPatternCondition(state.Left(address), 1) +
                    2 * state.ChromaPatternCondition(state.Top(address), 1));
            }
            state.Patterns[address] = (byte)pattern;
            if (pattern == 0) { previousDelta = 0; }
        }

        public void WriteQuantizerDelta(int delta)
        {
            if (delta is < -26 or > 25) { throw new ArgumentOutOfRangeException(nameof(delta)); }
            int mapped = delta <= 0 ? -2 * delta : 2 * delta - 1;
            WriteBin(mapped != 0, previousDelta == 0 ? 60 : 61);
            for (int index = 1; index <= mapped; index++) { WriteBin(index < mapped, index == 1 ? 62 : 63); }
            previousDelta = delta;
        }

        public void WriteMotionDifference(int difference, bool vertical, int neighbourSum)
        {
            int offset = vertical ? 47 : 40;
            int neighbour = neighbourSum > 32 ? 2 : neighbourSum > 2 ? 1 : 0;
            int magnitude = Math.Abs(difference);
            WriteBin(magnitude != 0, offset + neighbour);
            if (magnitude == 0) { return; }
            for (int index = 1; index < 9; index++)
            {
                WriteBin(magnitude > index, offset + Math.Min(index + 2, 6));
                if (magnitude == index) { break; }
            }
            if (magnitude >= 9) { WriteExpGolomb(magnitude - 9, 3); }
            encoder.WriteBypass(difference < 0);
        }

        private void WriteExpGolomb(int value, int order)
        {
            while (value >= 1 << order)
            {
                encoder.WriteBypass(true); value -= 1 << order++;
            }
            encoder.WriteBypass(false);
            encoder.WriteBypassBits((uint)value, order);
        }
    }
}
