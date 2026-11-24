// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264SkipResidualBounds(byte Luma, byte Chroma)
    {
        // Bound visible error of early P_SKIP even when a larger residual would quantize to zero.
        private const byte MaximumSkipSampleError = 1;

        public bool AllowsStationarySkip(H264SourceMacroblock source, H264Picture reference, int x, int y)
        {
            return MatchesLuma(source.Luma, reference, x, y) &&
                MatchesChroma(source.Chroma[..64], reference, 1, x / 2, y / 2) &&
                MatchesChroma(source.Chroma[64..], reference, 2, x / 2, y / 2);
        }

        private bool MatchesLuma(ReadOnlySpan<byte> source, H264Picture reference, int x, int y)
        {
            int stride = reference.Width;
            ReadOnlySpan<byte> plane = reference.ReadPlane(0)[(y * stride + x)..];
            Vector128<byte> bound = Vector128.Create(Math.Min(Luma, MaximumSkipSampleError));
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * 16));
                Vector128<byte> previous = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(plane), (nuint)(row * stride));
                if (Vector128.GreaterThanAny(Vector128.Max(original, previous) - Vector128.Min(original, previous), bound)) { return false; }
            }
            return true;
        }

        private bool MatchesChroma(ReadOnlySpan<byte> source, H264Picture reference, int planeIndex, int x, int y)
        {
            int stride = reference.Width / 2;
            ReadOnlySpan<byte> plane = reference.ReadPlane(planeIndex)[(y * stride + x)..];
            Vector64<byte> bound = Vector64.Create(Math.Min(Chroma, MaximumSkipSampleError));
            for (int row = 0; row < 8; row++)
            {
                Vector64<byte> original = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * 8));
                Vector64<byte> previous = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(plane), (nuint)(row * stride));
                if (Vector64.GreaterThanAny(Vector64.Max(original, previous) - Vector64.Min(original, previous), bound)) { return false; }
            }
            return true;
        }
    }
}
