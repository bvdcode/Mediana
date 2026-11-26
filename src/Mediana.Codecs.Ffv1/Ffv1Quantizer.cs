// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Ffv1
{
    internal record Ffv1Quantizer(short[] Values, int ContextCount)
    {
        internal byte[]? InitialRangeState { get; set; }

        public int StateSize(bool rice) => ContextCount *
            (rice ? Unsafe.SizeOf<Ffv1RiceContext>() : Ffv1FrameModel.StateBytes);

        public void InitializeState(Span<byte> state, bool rice)
        {
            if (rice)
            {
                MemoryMarshal.Cast<byte, Ffv1RiceContext>(state).Fill(Ffv1RiceContext.Initial);
            }
            else if (InitialRangeState is byte[] initial)
            {
                initial.CopyTo(state);
            }
            else
            {
                state.Fill(128);
            }
        }
    }
}
