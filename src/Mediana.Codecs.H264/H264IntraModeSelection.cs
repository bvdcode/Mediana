// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraModeSelection
    {
        public static int Select(ReadOnlySpan<byte> input, H264Picture target, int x, int y, Span<byte> prediction)
        {
            Span<byte> top = stackalloc byte[8];
            Span<byte> left = stackalloc byte[4];
            Span<byte> source = stackalloc byte[16];
            Span<byte> candidate = stackalloc byte[16];
            (bool hasTop, bool hasLeft, byte? corner) = target.ReadLumaNeighbours(x, y, 4, top, left);
            H264Residuals.CopyPrediction(input, 16, 0, 0, source);
            Vector128<byte> sourcePixels = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source));
            int predicted = target.PredictedMode(x, y);
            int best = int.MaxValue;
            int selected = 2;
            for (int mode = 0; mode < 9; mode++)
            {
                if (!H264IntraPrediction.IsLumaModeAvailable(mode, hasTop, hasLeft, corner is not null))
                {
                    continue;
                }
                H264IntraPrediction.Luma4x4(mode, hasTop ? top : [], hasLeft ? left : [], corner, candidate);
                int difference = H264SampleDifference.Sum16(sourcePixels,
                    Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(candidate)));
                if (difference < best || (difference == best && mode == predicted))
                {
                    best = difference;
                    selected = mode;
                    candidate.CopyTo(prediction);
                }
            }
            return selected;
        }
    }
}
