// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264ChromaDifference
    {
        public static int Sum8x8(H264Picture reference, int x, int y, int motionX, int motionY,
            ReadOnlySpan<byte> source, int stop)
        {
            int stride = reference.Width / 2;
            int rows = reference.Height / 2;
            long left = (long)x / 2 + (motionX >> 3);
            long top = (long)y / 2 + (motionY >> 3);
            int dx = motionX & 7;
            int dy = motionY & 7;
            int side = (dx | dy) == 0 ? 8 : 9;
            if (left >= 0 && left <= stride - side && top >= 0 && top <= rows - side)
            {
                int start = checked((int)top * stride + (int)left);
                return Sum(reference.ReadPlane(1)[start..], reference.ReadPlane(2)[start..], stride, dx, dy, source, stop);
            }
            return Border(reference, left, top, side, dx, dy, source, stop);
        }

        private static int Border(H264Picture reference, long left, long top, int side, int dx, int dy,
            ReadOnlySpan<byte> source, int stop)
        {
            Span<byte> u = stackalloc byte[81];
            Span<byte> v = stackalloc byte[81];
            H264BorderExtension.Copy(reference.ReadPlane(1), reference.Width / 2, reference.Height / 2, left, top, side, side, u);
            H264BorderExtension.Copy(reference.ReadPlane(2), reference.Width / 2, reference.Height / 2, left, top, side, side, v);
            return Sum(u, v, side, dx, dy, source, stop);
        }

        private static int Sum(ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int stride, int dx, int dy,
            ReadOnlySpan<byte> source, int stop)
        {
            if ((dx | dy) == 0)
            {
                return Integer(u, v, stride, source, stop);
            }
            Vector128<short> horizontalWeight = Vector128.Create((short)dx);
            Vector128<short> verticalWeight = Vector128.Create((short)dy);
            Vector128<short> topU = H264ChromaInterpolation.Horizontal8(u, 0, horizontalWeight);
            Vector128<short> topV = H264ChromaInterpolation.Horizontal8(v, 0, horizontalWeight);
            int error = 0;
            for (int row = 0; row < 8; row++)
            {
                Vector128<short> bottomU = H264ChromaInterpolation.Horizontal8(u, (row + 1) * stride, horizontalWeight);
                Vector128<short> bottomV = H264ChromaInterpolation.Horizontal8(v, (row + 1) * stride, horizontalWeight);
                Vector128<ushort> valueU = Round(topU, bottomU, verticalWeight);
                Vector128<ushort> valueV = Round(topV, bottomV, verticalWeight);
                Vector128<byte> prediction = Vector128.Narrow(valueU, valueV);
                error += H264SampleDifference.Sum16(ReadPair(source, source[64..], row * 8), prediction);
                if (error > stop)
                {
                    break;
                }
                topU = bottomU;
                topV = bottomV;
            }
            return error;
        }

        private static int Integer(ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int stride, ReadOnlySpan<byte> source, int stop)
        {
            int error = 0;
            for (int row = 0; row < 8; row++)
            {
                error += H264SampleDifference.Sum16(ReadPair(source, source[64..], row * 8), ReadPair(u, v, row * stride));
                if (error > stop)
                {
                    break;
                }
            }
            return error;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<ushort> Round(Vector128<short> top, Vector128<short> bottom, Vector128<short> weight) =>
            Vector128.ShiftRightLogical((Vector128.ShiftLeft(top, 3) + (bottom - top) * weight + Vector128.Create((short)32)).AsUInt16(), 6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> ReadPair(ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int start)
        {
            ulong first = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(u), start));
            ulong second = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(v), start));
            return Vector128.Create(first, second).AsByte();
        }
    }
}
