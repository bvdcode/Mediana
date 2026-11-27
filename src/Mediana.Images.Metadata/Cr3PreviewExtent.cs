// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.IsoBmff;

namespace Mediana.Images.Metadata
{
    internal readonly record struct Cr3PreviewExtent(int Offset, int Length, int Width, int Height)
    {
        internal static Cr3PreviewExtent FromBox(uint type, int offset, int payloadLength, ReadOnlyMemory<byte> header)
        {
            ImageMetadataCursor cursor = new(header);
            uint version = cursor.Dword();
            ushort width, height; uint length;
            if (type == IsoBmffBox.Code("THMB"))
            {
                if (version is not (0 or 0x1000000)) { throw new NotSupportedException("Unknown Canon thumbnail version."); }
                width = cursor.Word(); height = cursor.Word(); length = cursor.Dword();
                if (version == 0) { cursor.Take(4); }
            }
            else
            {
                if (version != 0) { throw new NotSupportedException("Unknown Canon preview version."); }
                cursor.Take(2); width = cursor.Word(); height = cursor.Word(); cursor.Take(2); length = cursor.Dword();
            }
            if (width == 0 || height == 0 || length == 0) { throw new InvalidDataException("Invalid Canon JPEG preview declaration."); }
            int prefixLength = header.Length - cursor.Remaining;
            if (length > payloadLength - prefixLength) { throw new InvalidDataException("Canon JPEG exceeds its containing box."); }
            return new(checked(offset + prefixLength), (int)length, width, height);
        }
    }
}
