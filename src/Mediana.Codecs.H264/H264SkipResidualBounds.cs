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
            int lumaStride = reference.Width;
            if (!MatchesLuma(source.Luma, 16, reference.ReadPlane(0)[(y * lumaStride + x)..], lumaStride))
            {
                return false;
            }
            int chromaStride = lumaStride / 2;
            int chromaStart = y / 2 * chromaStride + x / 2;
            return MatchesChroma(source.Chroma[..64], 8, reference.ReadPlane(1)[chromaStart..], chromaStride) &&
                MatchesChroma(source.Chroma[64..], 8, reference.ReadPlane(2)[chromaStart..], chromaStride);
        }

        internal bool MatchesLuma(ReadOnlySpan<byte> source, int sourceStride, ReadOnlySpan<byte> reference, int referenceStride)
        {
            // An RMS error of one permits at most one block width of error in any single sample.
            Vector128<byte> bound = Vector128.Create(Math.Min(Luma, (byte)16));
            Vector128<ushort> energy = Vector128<ushort>.Zero;
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * sourceStride));
                Vector128<byte> previous = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(reference), (nuint)(row * referenceStride));
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

        internal bool MatchesChroma(ReadOnlySpan<byte> source, int sourceStride, ReadOnlySpan<byte> reference, int referenceStride)
        {
            Vector64<byte> bound = Vector64.Create(Math.Min(Chroma, (byte)8));
            Vector128<ushort> energy = Vector128<ushort>.Zero;
            for (int row = 0; row < 8; row++)
            {
                Vector64<byte> original = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * sourceStride));
                Vector64<byte> previous = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(reference), (nuint)(row * referenceStride));
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
