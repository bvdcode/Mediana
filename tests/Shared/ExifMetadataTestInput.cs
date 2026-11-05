// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;

namespace Mediana.Tests
{
    internal static class ExifMetadataTestInput
    {
        internal static byte[] Create(bool little)
        {
            byte[] data = new byte[512];
            data[0] = data[1] = little ? (byte)73 : (byte)77;
            Short(2, 42); Long(4, 8); Short(8, 3); Short(50, 3); Short(92, 2);
            int end = 122;
            byte[] make = Encoding.UTF8.GetBytes("Камера\0");
            Entry(10, 271, 129, (uint)make.Length, Payload(make));
            Entry(22, 34665, 4, 1, 50);
            Entry(34, 34853, 4, 1, 92);
            byte[] fraction = new byte[8];
            WriteLong(fraction, 0, 1); WriteLong(fraction, 4, 125);
            Entry(52, 33434, 5, 1, Payload(fraction));
            Entry(64, 36867, 2, 20, Payload("2026:02:23 11:05:42\0"u8));
            Entry(76, 37500, 7, 3, 0); data[84] = 9; data[85] = 8; data[86] = 7;
            Entry(94, 1, 2, 2, 0); data[102] = (byte)'N';
            byte[] latitude = new byte[24];
            WriteLong(latitude, 0, 40); WriteLong(latitude, 4, 1);
            WriteLong(latitude, 8, 30); WriteLong(latitude, 12, 1);
            WriteLong(latitude, 16, 1); WriteLong(latitude, 20, 2);
            Entry(106, 2, 5, 3, Payload(latitude));
            return data[..end];

            void Short(int offset, ushort value)
            {
                if (little) { BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value); }
                else { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value); }
            }
            void Long(int offset, uint value) => WriteLong(data, offset, value);
            void WriteLong(byte[] target, int offset, uint value)
            {
                if (little) { BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), value); }
                else { BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset), value); }
            }
            uint Payload(ReadOnlySpan<byte> value)
            {
                int start = end;
                value.CopyTo(data.AsSpan(start)); end += value.Length;
                if ((end & 1) != 0) { end++; }
                return (uint)start;
            }
            void Entry(int offset, ushort tag, ushort type, uint count, uint value)
            {
                Short(offset, tag); Short(offset + 2, type); Long(offset + 4, count); Long(offset + 8, value);
            }
        }

        internal static void Verify()
        {
            foreach (bool little in new[] { false, true })
            {
                byte[] data = Create(little);
                MediaMetadata metadata = ExifMetadataReader.Read(data);
                Array.Clear(data);
                MediaProperty make = metadata.Children.Single(directory => directory.Scope == "IFD0").Properties.Single(property => property.Name == "Make");
                MediaMetadata exif = metadata.Children.Single(directory => directory.Scope == "Exif");
                MediaMetadata gps = metadata.Children.Single(directory => directory.Scope == "GPS");
                if (make.Values[0].AsText() != "Камера" ||
                    exif.Properties.Single(property => property.Name == "ExposureTime").Values[0].AsRational() != (1, 125) ||
                    exif.Properties.Single(property => property.Name == "DateTimeOriginal").Values[0].AsText() != "2026:02:23 11:05:42" ||
                    !exif.Properties.Single(property => property.Name == "MakerNote").Values[0].AsBytes().Span.SequenceEqual(new byte[] { 9, 8, 7 }) ||
                    gps.Properties.Single(property => property.Name == "GPSLatitude").Values[2].AsRational() != (1, 2))
                {
                    throw new InvalidDataException("Exif values differ from the authored native metadata.");
                }
            }
        }
    }
}
