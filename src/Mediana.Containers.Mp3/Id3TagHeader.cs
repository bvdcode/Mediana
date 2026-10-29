// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp3
{
    internal record Id3TagHeader(int Version, int Flags, int BodyBytes)
    {
        public bool HasFooter => Version == 4 && (Flags & 0x10) != 0;

        public static Id3TagHeader Read(ReadOnlySpan<byte> data, Mp3ReadOptions options)
        {
            if (data.Length != 10 || !data[..3].SequenceEqual("ID3"u8) || data[4] == 255)
            {
                throw new InvalidDataException("The ID3 header is invalid.");
            }
            int version = data[3];
            if (version is not (3 or 4) || data[4] != 0)
            {
                throw new NotSupportedException("Only ID3v2.3.0 and ID3v2.4.0 are supported.");
            }
            int flags = data[5];
            int allowed = version switch
            {
                3 => 0xe0,
                4 => 0xf0,
                _ => throw new InvalidOperationException()
            };
            if ((flags & ~allowed) != 0)
            {
                throw new InvalidDataException("The ID3 header uses reserved flags.");
            }
            int length = Id3Binary.ReadSynchsafe(data[6..]);
            Id3TagHeader header = new(version, flags, length);
            if (length > options.MaxMetadataBytes - 10 - (header.HasFooter ? 10 : 0))
            {
                throw new NotSupportedException("The ID3 tag exceeds its configured byte limit.");
            }
            return header;
        }
    }
}
