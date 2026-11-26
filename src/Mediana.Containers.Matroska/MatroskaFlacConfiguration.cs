// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaFlacConfiguration
    {
        public static ReadOnlySpan<byte> ReadStreamInfo(ReadOnlySpan<byte> data)
        {
            if (!data.StartsWith("fLaC"u8))
            {
                throw new InvalidDataException("The Matroska FLAC configuration lacks its stream marker.");
            }
            const int headerBytes = 4;
            ReadOnlySpan<byte> blocks = data[headerBytes..];
            ReadOnlySpan<byte> streamInfo = default;
            while (blocks.Length >= headerBytes)
            {
                int type = blocks[0] & 0x7f;
                bool final = (blocks[0] & 0x80) != 0;
                int length = blocks[1] << 16 | blocks[2] << 8 | blocks[3];
                if (type == 127 || length > blocks.Length - headerBytes ||
                    (streamInfo.IsEmpty && (type != 0 || length != FlacConfiguration.StreamInfoBytes)) ||
                    (!streamInfo.IsEmpty && type == 0))
                {
                    throw new InvalidDataException("The Matroska FLAC metadata block sequence is invalid.");
                }
                if (type == 0)
                {
                    streamInfo = blocks.Slice(headerBytes, length);
                }
                blocks = blocks[(headerBytes + length)..];
                if (final && blocks.IsEmpty)
                {
                    return streamInfo;
                }
                if (final)
                {
                    break;
                }
            }
            throw new InvalidDataException("The Matroska FLAC configuration does not end at its final metadata block.");
        }
    }
}
