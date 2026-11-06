// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Codecs.Bmp
{
    internal class DibImage
    {
        internal readonly ReadOnlyMemory<byte> Data;
        internal readonly int Width, Height, Depth, PixelOffset, PaletteOffset, PaletteStride, PaletteCount;
        internal readonly bool TopDown, Icon;
        internal readonly uint Compression, Red, Green, Blue, Alpha;
        internal readonly VideoColorInfo Color;
        internal readonly VideoColorDescription? ColorDescription;

        internal DibImage(ReadOnlyMemory<byte> data, int pixelOffset, bool icon)
        {
            Data = data; Icon = icon;
            int header = checked((int)U32(0));
            if (header is not (12 or 40 or 52 or 56 or 108 or 124))
            { throw new NotSupportedException($"DIB header size {header} is not supported."); }
            Require(0, header);
            Width = header == 12 ? U16(4) : I32(4);
            int signedHeight = header == 12 ? U16(6) : I32(8);
            if (Width <= 0 || signedHeight is 0 or int.MinValue || (icon && (signedHeight < 0 || signedHeight % 2 != 0)))
            { throw new InvalidDataException("Invalid DIB dimensions."); }
            TopDown = signedHeight < 0;
            Height = Math.Abs(signedHeight) / (icon ? 2 : 1);
            if (U16(header == 12 ? 8 : 12) != 1) { throw new InvalidDataException("DIB requires one plane."); }
            Depth = U16(header == 12 ? 10 : 14);
            if (Depth is not (1 or 4 or 8 or 16 or 24 or 32 or 0)) { throw new NotSupportedException("Unsupported DIB bit depth."); }
            Compression = header == 12 ? 0 : U32(16);
            if (Compression is > 6 || (TopDown && Compression is not (0 or 3 or 6)))
            { throw new NotSupportedException("Unsupported DIB compression or scan direction."); }
            if ((Compression == 1 && Depth != 8) || (Compression == 2 && Depth != 4)
                || (Compression is 3 or 6 && Depth is not (16 or 32)) || (Compression == 0 && Depth == 0))
            { throw new InvalidDataException("DIB compression does not match its bit depth."); }
            int masks = Compression is 3 or 6 && header == 40 ? (Compression == 6 ? 16 : 12) : 0;
            Require(header, masks);
            if (Compression is 3 or 6)
            {
                Red = U32(40); Green = U32(44); Blue = U32(48);
                Alpha = header >= 56 || masks == 16 ? U32(52) : 0;
                ValidateMask(Red); ValidateMask(Green); ValidateMask(Blue);
                if (Alpha != 0) { ValidateMask(Alpha); }
                if ((Red & Green) != 0 || (Red & Blue) != 0 || (Green & Blue) != 0 || ((Red | Green | Blue) & Alpha) != 0)
                { throw new InvalidDataException("Overlapping DIB component masks."); }
            }
            else if (Depth == 16) { Red = 0x7c00; Green = 0x3e0; Blue = 0x1f; }
            else if (Depth == 32) { Red = 0xff0000; Green = 0xff00; Blue = 0xff; Alpha = icon ? 0xff000000 : 0; }
            PaletteOffset = checked(header + masks); PaletteStride = header == 12 ? 3 : 4;
            uint colors = header == 12 ? 0 : U32(32);
            PaletteCount = Depth is > 0 and <= 8 ? colors == 0 ? 1 << Depth : checked((int)colors) : 0;
            if (Depth is > 0 and <= 8 && PaletteCount > 1 << Depth) { throw new InvalidDataException("DIB palette exceeds its index precision."); }
            Require(PaletteOffset, checked(PaletteCount * PaletteStride));
            int minimumOffset = checked(PaletteOffset + PaletteCount * PaletteStride);
            PixelOffset = pixelOffset < 0 ? minimumOffset : pixelOffset;
            if (PixelOffset < minimumOffset || PixelOffset > data.Length) { throw new InvalidDataException("Invalid DIB pixel offset."); }
            Color = VideoColorInfo.Srgb;
            if (header >= 108)
            {
                uint colorSpace = U32(56);
                if (colorSpace == 0x4d424544 && header == 124)
                {
                    int profileOffset = checked((int)U32(112)), profileLength = checked((int)U32(116));
                    Require(profileOffset, profileLength);
                    ColorDescription = new IccColorDescription(Data.Span.Slice(profileOffset, profileLength), false);
                }
                else if (colorSpace is not (0x73524742 or 0x57696e20))
                { Color = new(); }
            }
        }

        private void ValidateMask(uint mask)
        {
            if (mask == 0 || (Depth == 16 && mask > ushort.MaxValue)) { throw new InvalidDataException("Invalid DIB component mask."); }
            uint shifted = mask >> BitOperations.TrailingZeroCount(mask);
            if ((shifted & (shifted + 1)) != 0) { throw new InvalidDataException("DIB component mask is not contiguous."); }
        }
        internal void Require(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > Data.Length - length) { throw new InvalidDataException("Truncated DIB data."); }
        }
        internal ushort U16(int offset) { Require(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Data.Span[offset..]); }
        internal uint U32(int offset) { Require(offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Data.Span[offset..]); }
        private int I32(int offset) => unchecked((int)U32(offset));
        internal uint Palette(int index)
        {
            if ((uint)index >= PaletteCount) { throw new InvalidDataException("DIB palette index is out of range."); }
            int offset = PaletteOffset + index * PaletteStride;
            return 0xff000000u | (uint)(Data.Span[offset + 2] << 16 | Data.Span[offset + 1] << 8 | Data.Span[offset]);
        }
    }
}
