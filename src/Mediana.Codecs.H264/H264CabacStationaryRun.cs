// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264CabacStationaryRun
    {
        public static int Read(ref H264BitReader bits, H264CabacReader reader, ReadOnlySpan<byte> types,
            int first, int rowEnd, int sliceEnd, int columns, int sliceStart, CancellationToken token,
            out bool nonSkippedFlagRead)
        {
            int next = first + 1;
            nonSkippedFlagRead = false;
            while (true)
            {
                reader.ReadSliceBoundary(ref bits, next == sliceEnd);
                if (next == rowEnd)
                {
                    return next;
                }
                token.ThrowIfCancellationRequested();
                // A zero-vector skip on the left forces the same predictor for the next P-skip.
                int top = next - columns;
                int context = top >= sliceStart && types[top] != H264CabacMacroblockState.Skipped ? 1 : 0;
                if (!H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, context))
                {
                    nonSkippedFlagRead = true;
                    return next;
                }
                next++;
            }
        }
    }
}
