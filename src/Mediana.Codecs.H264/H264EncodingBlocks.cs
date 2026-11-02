// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264EncodingBlocks
    {
        public static void Transform(ReadOnlySpan<byte> source, int sourceStride,
            ReadOnlySpan<byte> prediction, int predictionStride, Span<int> transformed)
        {
            Vector128<byte> samples = Read4x4(source, sourceStride);
            Vector128<byte> predicted = Read4x4(prediction, predictionStride);
            Vector128<short> lower = Vector128.Subtract(Vector128.WidenLower(samples), Vector128.WidenLower(predicted)).AsInt16();
            Vector128<short> upper = Vector128.Subtract(Vector128.WidenUpper(samples), Vector128.WidenUpper(predicted)).AsInt16();
            Span<int> residual = stackalloc int[16];
            Vector128.WidenLower(lower).StoreUnsafe(ref MemoryMarshal.GetReference(residual));
            Vector128.WidenUpper(lower).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 4);
            Vector128.WidenLower(upper).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 8);
            Vector128.WidenUpper(upper).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 12);
            H264ForwardTransform.Transform4x4(residual, transformed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Read4x4(ReadOnlySpan<byte> source, int stride) => Vector128.Create(
            MemoryMarshal.Read<uint>(source),
            MemoryMarshal.Read<uint>(source[stride..]),
            MemoryMarshal.Read<uint>(source[(stride * 2)..]),
            MemoryMarshal.Read<uint>(source[(stride * 3)..])).AsByte();
    }
}
