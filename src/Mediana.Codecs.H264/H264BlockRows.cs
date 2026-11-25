// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264BlockRows
    {
        public static void Copy(ReadOnlySpan<byte> source, int sourceStride, Span<byte> destination, int destinationStride,
            int rowBytes, int rows)
        {
            switch (rowBytes)
            {
                case 2:
                    CopyRows<ushort>(source, sourceStride, destination, destinationStride, rows);
                    break;
                case 4:
                    CopyRows<uint>(source, sourceStride, destination, destinationStride, rows);
                    break;
                case 8:
                    CopyRows<ulong>(source, sourceStride, destination, destinationStride, rows);
                    break;
                case 16:
                    CopyRows<Vector128<byte>>(source, sourceStride, destination, destinationStride, rows);
                    break;
                case 32:
                    CopyRows<Vector256<byte>>(source, sourceStride, destination, destinationStride, rows);
                    break;
                default:
                    for (int row = 0; row < rows; row++)
                    {
                        source.Slice(row * sourceStride, rowBytes).CopyTo(destination.Slice(row * destinationStride, rowBytes));
                    }
                    break;
            }
        }

        private static void CopyRows<T>(ReadOnlySpan<byte> source, int sourceStride, Span<byte> destination,
            int destinationStride, int rows) where T : unmanaged
        {
            for (int row = 0; row < rows; row++)
            {
                T value = MemoryMarshal.Read<T>(source[(row * sourceStride)..]);
                MemoryMarshal.Write(destination[(row * destinationStride)..], in value);
            }
        }
    }
}
