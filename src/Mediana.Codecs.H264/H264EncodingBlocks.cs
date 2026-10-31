// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264EncodingBlocks
    {
        public static void Transform(VideoFrame input, int plane, int x, int y, ReadOnlySpan<byte> prediction, Span<int> transformed)
        {
            Span<byte> source = stackalloc byte[16];
            H264BorderExtension.Copy(input, plane, x, y, 4, 4, source);
            Vector128<byte> samples = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source));
            Vector128<byte> predicted = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(prediction[..16]));
            Vector128<short> lower = Vector128.Subtract(Vector128.WidenLower(samples), Vector128.WidenLower(predicted)).AsInt16();
            Vector128<short> upper = Vector128.Subtract(Vector128.WidenUpper(samples), Vector128.WidenUpper(predicted)).AsInt16();
            Span<int> residual = stackalloc int[16];
            Vector128.WidenLower(lower).StoreUnsafe(ref MemoryMarshal.GetReference(residual));
            Vector128.WidenUpper(lower).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 4);
            Vector128.WidenLower(upper).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 8);
            Vector128.WidenUpper(upper).StoreUnsafe(ref MemoryMarshal.GetReference(residual), 12);
            H264ForwardTransform.Transform4x4(residual, transformed);
        }
    }
}
