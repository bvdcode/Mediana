// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264SkipResidualBounds(byte Luma, byte Chroma)
    {
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
            // An RMS error of one permits at most one block width of error in any single sample.
            Vector128<byte> bound = Vector128.Create(Math.Min(Luma, (byte)16));
            Vector128<ushort> energy = Vector128<ushort>.Zero;
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * 16));
                Vector128<byte> previous = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(plane), (nuint)(row * stride));
                Vector128<byte> delta = Vector128.Max(original, previous) - Vector128.Min(original, previous);
                if (Vector128.GreaterThanAny(delta, bound))
                {
                    return false;
                }
                Vector128<ushort> low = Vector128.WidenLower(delta);
                Vector128<ushort> high = Vector128.WidenUpper(delta);
                energy += low * low + high * high;
            }
            return Vector128.Sum(Vector128.WidenLower(energy) + Vector128.WidenUpper(energy)) <= 16 * 16;
        }

        private bool MatchesChroma(ReadOnlySpan<byte> source, H264Picture reference, int planeIndex, int x, int y)
        {
            int stride = reference.Width / 2;
            ReadOnlySpan<byte> plane = reference.ReadPlane(planeIndex)[(y * stride + x)..];
            Vector64<byte> bound = Vector64.Create(Math.Min(Chroma, (byte)8));
            Vector128<ushort> energy = Vector128<ushort>.Zero;
            for (int row = 0; row < 8; row++)
            {
                Vector64<byte> original = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * 8));
                Vector64<byte> previous = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(plane), (nuint)(row * stride));
                Vector64<byte> delta = Vector64.Max(original, previous) - Vector64.Min(original, previous);
                if (Vector64.GreaterThanAny(delta, bound))
                {
                    return false;
                }
                Vector128<ushort> differences = Vector128.WidenLower(delta.ToVector128());
                energy += differences * differences;
            }
            return Vector128.Sum(energy) <= 8 * 8;
        }
    }
}
