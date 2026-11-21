// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Processing
{
    internal class IccMatrixProfile
    {
        internal bool Gray { get; }
        internal RgbMatrix Matrix { get; }
        internal double[] Red { get; }
        internal double[] Green { get; }
        internal double[] Blue { get; }

        internal IccMatrixProfile(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            IccProfile profile = new(data, token);
            if (profile.ConnectionSpace != U32("XYZ "u8))
            { throw new NotSupportedException("ICC matrix conversion requires an XYZ connection space."); }
            if (profile.HasTag("A2B0"u8) || profile.HasTag("A2B1"u8) || profile.HasTag("A2B2"u8) || profile.HasTag("D2B0"u8) ||
                profile.HasTag("D2B1"u8) || profile.HasTag("D2B2"u8) || profile.HasTag("D2B3"u8))
            {
                throw new NotSupportedException("ICC lookup-table and multi-process transforms are not supported.");
            }
            uint space = profile.ColorSpace;
            if (space == U32("RGB "u8))
            {
                (double rx, double ry, double rz) = Xyz(profile, "rXYZ"u8);
                (double gx, double gy, double gz) = Xyz(profile, "gXYZ"u8);
                (double bx, double by, double bz) = Xyz(profile, "bXYZ"u8);
                Matrix = new(rx, gx, bx, ry, gy, by, rz, gz, bz);
                double determinant = rx * (gy * bz - gz * by) - gx * (ry * bz - rz * by) + bx * (ry * gz - rz * gy);
                if (determinant == 0) { throw new InvalidDataException("ICC RGB colorant matrix is singular."); }
                ReadOnlyMemory<byte> red = profile.Tag("rTRC"u8), green = profile.Tag("gTRC"u8), blue = profile.Tag("bTRC"u8);
                Red = IccToneCurve.Sample(red, token);
                Green = green.Equals(red) ? Red : IccToneCurve.Sample(green, token);
                Blue = blue.Equals(red) ? Red : blue.Equals(green) ? Green : IccToneCurve.Sample(blue, token);
            }
            else if (space == U32("GRAY"u8))
            {
                Gray = true;
                Matrix = new(0.9642, 0, 0, 1, 0, 0, 0.8249, 0, 0);
                Red = IccToneCurve.Sample(profile.Tag("kTRC"u8), token);
                Green = Blue = Red;
            }
            else { throw new NotSupportedException("ICC matrix conversion requires RGB or grayscale samples."); }
        }

        private static (double X, double Y, double Z) Xyz(IccProfile profile, ReadOnlySpan<byte> signature)
        {
            ReadOnlySpan<byte> tag = profile.Tag(signature).Span;
            if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8) || U32(tag[4..]) != 0)
            {
                throw new InvalidDataException("ICC matrix colorant is not a valid XYZ value.");
            }
            return (IccToneCurve.Fixed(tag[8..]), IccToneCurve.Fixed(tag[12..]), IccToneCurve.Fixed(tag[16..]));
        }

        private static uint U32(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32BigEndian(value);
    }
}
