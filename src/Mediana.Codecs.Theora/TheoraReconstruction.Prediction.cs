// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.Theora
{
    internal static partial class TheoraReconstruction
    {
        private static void Predict(TheoraLayout layout, int plane, int x, int y, int mx, int my,
            int reference, ReadOnlySpan<byte> source, Span<byte> target)
        {
            int width = layout.PlaneWidth(plane), height = layout.PlaneHeight(plane), offset = layout.PlaneOffset(plane);
            int row = (height - 1 - y) * width + x;
            if (reference == 0)
            {
                for (int i = 0; i < 8; i++) { target.Slice(row - i * width, 8).Fill(128); }
                return;
            }
            int dx = plane == 0 ? 2 : 2 * layout.Info.HorizontalSubsampling;
            int dy = plane == 0 ? 2 : 2 * layout.Info.VerticalSubsampling;
            int x1 = x + mx / dx, y1 = y + my / dy;
            int x2 = x + Math.Sign(mx) * ((Math.Abs(mx) + dx - 1) / dx);
            int y2 = y + Math.Sign(my) * ((Math.Abs(my) + dy - 1) / dy);
            bool average = x1 != x2 || y1 != y2;
            if (x1 >= 0 && x1 + 8 <= width && y1 >= 0 && y1 + 8 <= height &&
                x2 >= 0 && x2 + 8 <= width && y2 >= 0 && y2 + 8 <= height)
            {
                int start = offset + (height - 1 - y1) * width + x1;
                int second = offset + (height - 1 - y2) * width + x2;
                for (int i = 0; i < 8; i++)
                {
                    ReadOnlySpan<byte> a = source.Slice(start - i * width, 8); Span<byte> destination = target.Slice(row - i * width, 8);
                    if (!average) { a.CopyTo(destination); }
                    else if (Vector128.IsHardwareAccelerated)
                    {
                        Vector128<byte> first = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(a)).ToVector128();
                        Vector128<byte> last = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(source.Slice(second - i * width, 8))).ToVector128();
                        Vector128<byte> result = (first & last) + ((first ^ last) >>> 1);
                        result.GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(destination));
                    }
                    else
                    {
                        ReadOnlySpan<byte> b = source.Slice(second - i * width, 8);
                        for (int c = 0; c < 8; c++) { destination[c] = (byte)((a[c] + b[c]) >> 1); }
                    }
                }
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                int firstRow = offset + (height - 1 - Math.Clamp(y1 + i, 0, height - 1)) * width;
                int secondRow = offset + (height - 1 - Math.Clamp(y2 + i, 0, height - 1)) * width;
                for (int c = 0; c < 8; c++)
                {
                    int value = source[firstRow + Math.Clamp(x1 + c, 0, width - 1)];
                    if (average) { value = (value + source[secondRow + Math.Clamp(x2 + c, 0, width - 1)]) >> 1; }
                    target[row - i * width + c] = (byte)value;
                }
            }
        }

        private static void AddResidual(Span<byte> target, int width, int height, int x, int y, ReadOnlySpan<short> residual, bool constant, int dc)
        {
            if (constant && dc == 0) { return; }
            int row = (height - 1 - y) * width + x;
            if (constant && (dc <= -255 || dc >= 255))
            {
                byte value = dc >= 255 ? (byte)255 : (byte)0;
                for (int i = 0; i < 8; i++) { target.Slice(row - i * width, 8).Fill(value); }
                return;
            }
            if (constant && Vector128.IsHardwareAccelerated)
            {
                Vector128<short> delta = Vector128.Create((short)dc), maximum = Vector128.Create((short)255);
                for (int i = 0; i < 8; i++)
                {
                    Span<byte> destination = target.Slice(row - i * width, 8);
                    Vector128<byte> input = Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(destination)).ToVector128();
                    Vector128<short> value = Vector128.Min(Vector128.Max(Vector128.WidenLower(input).AsInt16() + delta, Vector128<short>.Zero), maximum);
                    Vector128.Narrow(value.AsUInt16(), Vector128<ushort>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(destination));
                }
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int index = row - i * width + c;
                    target[index] = (byte)Math.Clamp(target[index] + (constant ? dc : residual[i * 8 + c]), 0, 255);
                }
            }
        }
    }
}
