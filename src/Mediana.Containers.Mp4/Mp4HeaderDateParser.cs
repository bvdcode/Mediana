// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4HeaderDateParser
    {
        public static Mp4HeaderDates Read(ReadOnlySpan<byte> header, DateTimeKind kind) => header[0] switch
        {
            0 => new(Mp4Binary.UInt32(header, 4), Mp4Binary.UInt32(header, 8), kind),
            1 => new(Mp4Binary.UInt64(header, 4), Mp4Binary.UInt64(header, 12), kind),
            _ => throw new NotSupportedException("The MP4 header version is not supported.")
        };
    }
}
