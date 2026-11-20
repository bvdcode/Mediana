// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal struct H264CabacPairSyntax(H264Picture picture, H264SliceHeader header,
        H264CabacReader reader, H264CabacMacroblockState state)
    {
        private bool bottomSkipped;
        private bool bottomPrefetched;

        public bool ReadSkip(ref H264BitReader bits, int address)
        {
            if (!picture.Layout.MacroblockAdaptiveFrameField)
            {
                return ReadFlag(ref bits, state.SkipContext(address));
            }
            if ((address & 1) != 0)
            {
                if (bottomPrefetched)
                {
                    bottomPrefetched = false;
                    return bottomSkipped;
                }
                return ReadFlag(ref bits, state.SkipContext(address));
            }
            if (header.Kind == H264SliceKind.I)
            {
                picture.Grid.SetFieldPair(address, reader.Read(ref bits, state.FieldPairContext(address)));
                return false;
            }
            bool inferred = picture.Grid.InferFieldPair(address, header.FirstMacroblock);
            bool skipped = ReadFlag(ref bits, state.SkipContext(address, inferred));
            if (skipped)
            {
                state.Types[address] = H264CabacMacroblockState.Skipped;
                bottomSkipped = ReadFlag(ref bits, state.SkipContext(address + 1, inferred));
                bottomPrefetched = true;
            }
            bool field = skipped && bottomSkipped ? inferred : reader.Read(ref bits, state.FieldPairContext(address));
            picture.Grid.SetFieldPair(address, field);
            return skipped;
        }

        private bool ReadFlag(ref H264BitReader bits, int context) => header.Kind switch
        {
            H264SliceKind.I => false,
            H264SliceKind.P => H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, context),
            H264SliceKind.B => H264CabacMacroblockSyntax.ReadBidirectionalSkip(ref bits, reader, context),
            _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
        };
    }
}
