// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class MeshGeometryTestInput
    {
        internal static byte[] BinaryTriangle() => BinaryTriangles(1);

        internal static byte[] BinaryTriangles(int count)
        {
            byte[] data = new byte[checked(84 + (50 * count))];
            "solid binary header"u8.CopyTo(data);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), (uint)count);
            for (int index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(108 + (index * 50)), 1);
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(124 + (index * 50)), 1);
            }
            return data;
        }
    }
}
