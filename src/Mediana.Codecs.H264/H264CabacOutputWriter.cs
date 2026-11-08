// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CabacOutputWriter(H264CabacEncoder encoder) : IH264CabacBinWriter
    {
        public H264CabacEncoder Encoder { get; } = encoder;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBin(bool value, ref H264CabacContext context) => Encoder.WriteBin(value, ref context);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBypass(bool value) => Encoder.WriteBypass(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBypassBits(uint value, int count) => Encoder.WriteBypassBits(value, count);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTermination(bool value) => Encoder.WriteTermination(value);
    }
}
