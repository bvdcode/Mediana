// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264SampleAverage
    {
        internal static bool SupportsByteShuffle => BitConverter.IsLittleEndian && (Ssse3.IsSupported || AdvSimd.Arm64.IsSupported);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<byte> Quarter(Vector128<byte> a, Vector128<byte> b, Vector128<byte> c)
        {
            Vector128<byte> floor = Rounded(a, c) - ((a ^ c) & Vector128.Create((byte)1));
            return Rounded(floor, b);
        }

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
