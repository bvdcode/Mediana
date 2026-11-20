// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;

namespace Mediana.Tests
{
    internal static class Mp4TestEncoding
    {
        public static byte[] Write(Action<Mp4BoxWriter> write)
        {
            Mp4BoxWriter measure = new(Memory<byte>.Empty);
            write(measure);
            byte[] data = new byte[measure.Position];
            write(new(data));
            return data;
        }
    }
}
