// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Formats.Color
{
    /// <summary>ICC v2 display profiles for normalized sRGB samples and grayscale with the sRGB transfer curve.</summary>
    public static class IccProfiles
    {
        private static readonly byte[] RgbProfile = Create(false);
        private static readonly byte[] GrayProfile = Create(true);
        public static ReadOnlySpan<byte> Srgb => RgbProfile;
        public static ReadOnlySpan<byte> SrgbGray => GrayProfile;

        private static byte[] Create(bool gray)
        {
            const int curveSamples = 4096;
            const int curveBytes = 12 + curveSamples * 2;
            int tagCount = gray ? 5 : 10;
            int tableEnd = 132 + tagCount * 12;
            byte[] profile = new byte[tableEnd + 128 + 32 + 20 + 44 + (gray ? 0 : 60) + curveBytes];
            Span<byte> data = profile;
            U32(data, profile.Length);
            U32(data[8..], 0x02100000);
            "mntr"u8.CopyTo(data[12..]);
            (gray ? "GRAY"u8 : "RGB "u8).CopyTo(data[16..]);
            "XYZ "u8.CopyTo(data[20..]);
            BinaryPrimitives.WriteUInt16BigEndian(data[24..], 2026);
            BinaryPrimitives.WriteUInt16BigEndian(data[26..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(data[28..], 1);
            "acsp"u8.CopyTo(data[36..]);
            U32(data[64..], 1);
            XyzValues(data[68..], [0.9642, 1, 0.8249]);
            U32(data[128..], tagCount);
            int entry = 132;
            int offset = tableEnd;
            Span<byte> description = Add(data, "desc"u8, 128, ref entry, ref offset);
            "desc"u8.CopyTo(description);
            string name = gray ? "sRGB grayscale" : "sRGB";
            U32(description[8..], name.Length + 1);
            Encoding.ASCII.GetBytes(name, description[12..]);
            Span<byte> copyright = Add(data, "cprt"u8, 32, ref entry, ref offset);
            "text"u8.CopyTo(copyright);
            "MIT; Vadim Belov"u8.CopyTo(copyright[8..]);
            Span<byte> white = Add(data, "wtpt"u8, 20, ref entry, ref offset);
            "XYZ "u8.CopyTo(white);
            XyzValues(white[8..], [0.9642, 1, 0.8249]);
            Span<byte> adaptation = Add(data, "chad"u8, 44, ref entry, ref offset);
            "sf32"u8.CopyTo(adaptation);
            XyzValues(adaptation[8..], [1.047886, 0.022919, -0.050216, 0.029582, 0.990484, -0.017079, -0.009252, 0.015073, 0.751678]);
            if (!gray)
            {
                WriteColorant(data, "rXYZ"u8, [0.43603034, 0.22243847, 0.01389744], ref entry, ref offset);
                WriteColorant(data, "gXYZ"u8, [0.38510186, 0.71694275, 0.09707638], ref entry, ref offset);
                WriteColorant(data, "bXYZ"u8, [0.14306781, 0.06061878, 0.71392626], ref entry, ref offset);
            }
            int curveOffset = offset;
            Span<byte> curve = Add(data, gray ? "kTRC"u8 : "rTRC"u8, curveBytes, ref entry, ref offset);
            "curv"u8.CopyTo(curve);
            U32(curve[8..], curveSamples);
            for (int i = 0; i < curveSamples; i++)
            {
                double encoded = (double)i / (curveSamples - 1);
                double linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
                BinaryPrimitives.WriteUInt16BigEndian(curve[(12 + i * 2)..], (ushort)Math.Round(linear * 65535));
            }
            if (!gray)
            {
                Tag(data, "gTRC"u8, curveOffset, curveBytes, ref entry);
                Tag(data, "bTRC"u8, curveOffset, curveBytes, ref entry);
            }
            return profile;
        }

        private static void WriteColorant(Span<byte> data, ReadOnlySpan<byte> name, ReadOnlySpan<double> xyz, ref int entry, ref int offset)
        {
            Span<byte> tag = Add(data, name, 20, ref entry, ref offset);
            "XYZ "u8.CopyTo(tag);
            XyzValues(tag[8..], xyz);
        }

        private static Span<byte> Add(Span<byte> data, ReadOnlySpan<byte> name, int length, ref int entry, ref int offset)
        {
            Tag(data, name, offset, length, ref entry);
            Span<byte> result = data.Slice(offset, length);
            offset += length;
            return result;
        }

        private static void Tag(Span<byte> data, ReadOnlySpan<byte> name, int offset, int length, ref int entry)
        {
            name.CopyTo(data[entry..]);
            U32(data[(entry + 4)..], offset);
            U32(data[(entry + 8)..], length);
            entry += 12;
        }

        private static void XyzValues(Span<byte> data, ReadOnlySpan<double> values)
        {
            for (int i = 0; i < values.Length; i++) { BinaryPrimitives.WriteInt32BigEndian(data[(i * 4)..], (int)Math.Round(values[i] * 65536)); }
        }
        private static void U32(Span<byte> data, int value) => BinaryPrimitives.WriteInt32BigEndian(data, value);
    }
}
