// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264VectorSamples
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Read8(ReadOnlySpan<byte> samples, int position) =>
            Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), position))).AsByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write8(Span<byte> samples, int position, Vector128<byte> value) =>
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), position), value.AsUInt64().ToScalar());
    }
}
