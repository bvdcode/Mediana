// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264TransformMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Transpose(ref Vector128<int> first, ref Vector128<int> second,
            ref Vector128<int> third, ref Vector128<int> fourth)
        {
            Vector128<long> upperLeft = Vector128.ZipLower(first, second).AsInt64();
            Vector128<long> upperRight = Vector128.ZipUpper(first, second).AsInt64();
            Vector128<long> lowerLeft = Vector128.ZipLower(third, fourth).AsInt64();
            Vector128<long> lowerRight = Vector128.ZipUpper(third, fourth).AsInt64();
            first = Vector128.ZipLower(upperLeft, lowerLeft).AsInt32();
            second = Vector128.ZipUpper(upperLeft, lowerLeft).AsInt32();
            third = Vector128.ZipLower(upperRight, lowerRight).AsInt32();
            fourth = Vector128.ZipUpper(upperRight, lowerRight).AsInt32();
        }
    }
}
