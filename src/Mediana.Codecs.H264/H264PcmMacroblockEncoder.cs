// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264PcmMacroblockEncoder
    {
        // Rec. ITU-T H.264, A.3.1(n), CAVLC macroblock_layer bit limit.
        private const int MaximumMacroblockBits = 3200;

        public static void ReplaceOversized(H264BitWriter bits, in H264BitWriterCheckpoint checkpoint,
            VideoFrame input, H264Picture target, int x, int y, H264SliceKind kind)
        {
            if (bits.BitPosition - checkpoint.BitPosition <= MaximumMacroblockBits)
            {
                return;
            }
            uint type = kind switch
            {
                H264SliceKind.I => 25,
                H264SliceKind.P => 30,
                H264SliceKind.B or H264SliceKind.Sp or H264SliceKind.Si => throw new NotSupportedException("This PCM encoder requires an I or P slice."),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            bits.Restore(checkpoint);
            bits.WriteUnsigned(type);
            int alignment = (int)(bits.BitPosition & 7);
            if (alignment != 0)
            {
                bits.WriteBits(0, 8 - alignment);
            }
            target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, -1, 0, 0);
            for (int plane = 0; plane < 3; plane++)
            {
                WritePlane(bits, input, target, plane, plane == 0 ? x : x / 2, plane == 0 ? y : y / 2);
            }
            int address = y / 16 * target.Sequence.WidthInMbs + x / 16;
            target.Quantizers[address] = 0;
            target.Transforms8x8[address] = 0;
        }

        private static void WritePlane(H264BitWriter bits, VideoFrame input, H264Picture target, int plane, int x, int y)
        {
            int size = plane == 0 ? 16 : 8;
            int stride = target.Stride(plane);
            Span<byte> pixels = target.Plane(plane);
            Span<byte> samples = stackalloc byte[256];
            H264BorderExtension.Copy(input, plane, x, y, size, size, samples);
            for (int row = 0; row < size; row++)
            {
                ReadOnlySpan<byte> source = samples.Slice(row * size, size);
                for (int column = 0; column < size; column++)
                {
                    bits.WriteBits(source[column], 8);
                }
                source.CopyTo(pixels.Slice((y + row) * stride + x, size));
            }
            for (int row = 0; row < size; row += 4)
            {
                for (int column = 0; column < size; column += 4)
                {
                    int index = target.BlockIndex(plane, x + column, y + row);
                    target.Counts(plane)[index] = 16;
                    if (plane == 0)
                    {
                        target.Modes[index] = 2;
                    }
                }
            }
        }
    }
}
