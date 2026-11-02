// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    internal class PngColorChunks
    {
        private bool srgb;
        private bool icc;
        private uint? gamma;
        private uint[] chromaticities = [];
        private CicpColorDescription? cicp;

        public void Read(uint code, ReadOnlySpan<byte> data, bool beforeData, PngImageData image)
        {
            if (code is 0x73524742 or 0x63494350 or 0x67414d41 or 0x6348524d or 0x69434350)
            {
                if (!beforeData || !image.Palette.IsEmpty)
                {
                    throw new InvalidDataException("PNG color chunks must precede PLTE and IDAT.");
                }
            }
            switch (code)
            {
                case 0x73524742:
                    Require(data.Length == 1 && data[0] <= 3);
                    srgb = true;
                    break;
                case 0x63494350:
                    Require(data.Length == 4 && data[2] == 0 && data[3] <= 1);
                    cicp = new(data[0], data[1], data[2], data[3] == 1);
                    break;
                case 0x67414d41:
                    Require(data.Length == 4);
                    gamma = BinaryPrimitives.ReadUInt32BigEndian(data);
                    Require(gamma is > 0 and <= int.MaxValue);
                    break;
                case 0x6348524d:
                    Require(data.Length == 32);
                    chromaticities = new uint[8];
                    for (int index = 0; index < chromaticities.Length; index++)
                    {
                        chromaticities[index] = BinaryPrimitives.ReadUInt32BigEndian(data[(index * 4)..]);
                        Require(chromaticities[index] <= int.MaxValue);
                    }
                    break;
                case 0x69434350:
                    ReadIcc(data);
                    icc = true;
                    break;
                case 0x70485973:
                    Require(beforeData && data.Length == 9 && data[8] <= 1);
                    uint x = BinaryPrimitives.ReadUInt32BigEndian(data);
                    uint y = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    Require(x <= int.MaxValue && y <= int.MaxValue);
                    image.Aspect = x == 0 || y == 0 ? null : new((int)y, (int)x);
                    break;
                case 0x6163544c:
                    Require(beforeData && data.Length == 8 && BinaryPrimitives.ReadUInt32BigEndian(data) > 0);
                    image.Animated = true;
                    break;
                case 0x65584966:
                    Require(beforeData);
                    image.HasExif = true;
                    break;
            }
        }

        public void Apply(PngImageData image)
        {
            if (cicp is not null)
            {
                image.ColorDescription = cicp;
            }
            else if (icc)
            {
                image.ColorDescription = new PngColorDescription(gamma, chromaticities, true);
            }
            else if (srgb)
            {
                image.Color = VideoColorInfo.Srgb;
            }
            else if (gamma is not null || chromaticities.Length != 0)
            {
                image.ColorDescription = new PngColorDescription(gamma, chromaticities, false);
            }
        }

        private static void ReadIcc(ReadOnlySpan<byte> data)
        {
            int nameLength = data.IndexOf((byte)0);
            Require(nameLength is >= 1 and <= 79 && data.Length > nameLength + 2);
            ReadOnlySpan<byte> name = data[..nameLength];
            Require(name[0] != 32 && name[^1] != 32);
            for (int index = 0; index < name.Length; index++)
            {
                byte value = name[index];
                Require(value is >= 32 and <= 126 or >= 161 && !(value == 32 && index > 0 && name[index - 1] == 32));
            }
            Require(data[nameLength + 1] == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Invalid PNG ancillary chunk or chunk order.");
            }
        }
    }
}
