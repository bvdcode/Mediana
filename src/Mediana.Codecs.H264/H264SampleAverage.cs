// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264SampleAverage
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Rounded(Vector128<byte> left, Vector128<byte> right)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.Average(left, right);
            }
            Vector128<byte> difference = (left ^ right) & Vector128.Create((byte)254);
            return (left | right) - Vector128.ShiftRightLogical(difference.AsUInt16(), 1).AsByte();
        }
    }
}
