// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct EbmlElement(uint Id, long DataOffset, long? EndOffset)
    {
        public long? DataSize => EndOffset - DataOffset;

        public static EbmlElement ReadHeader(ReadOnlySpan<byte> header, long offset, long? parentEnd,
            int maximumIdWidth = 4, int maximumSizeWidth = 8)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (parentEnd is long end && (end < 0 || offset > end))
            {
                throw new InvalidDataException("The EBML element starts outside its parent.");
            }
            uint id = EbmlInteger.ReadId(header, out int idWidth, maximumIdWidth);
            long? size = EbmlInteger.ReadSize(header[idWidth..], out int sizeWidth, maximumSizeWidth);
            int headerSize = idWidth + sizeWidth;
            if (offset > long.MaxValue - headerSize)
            {
                throw new InvalidDataException("The EBML element header exceeds the addressable input.");
            }
            long dataOffset = offset + headerSize;
            if (parentEnd is long boundary && dataOffset > boundary)
            {
                throw new InvalidDataException("The EBML element header exceeds its parent.");
            }
            long? endOffset = null;
            if (size is long length)
            {
                if (length > long.MaxValue - dataOffset || (parentEnd is long limit && length > limit - dataOffset))
                {
                    throw new InvalidDataException("The EBML element data exceeds its parent or the addressable input.");
                }
                endOffset = dataOffset + length;
            }
            return new(id, dataOffset, endOffset);
        }
    }
}
