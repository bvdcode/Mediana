// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Processing
{
    internal class IccMatrixProfile
    {
        private readonly Dictionary<uint, ReadOnlyMemory<byte>> tags = [];
        internal bool Gray { get; }
        internal RgbMatrix Matrix { get; }
        internal double[] Red { get; }
        internal double[] Green { get; }
        internal double[] Blue { get; }

        internal IccMatrixProfile(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> bytes = data.Span;
            if (bytes.Length < 132 || !bytes.Slice(36, 4).SequenceEqual("acsp"u8))
            {
                throw new InvalidDataException("ICC profile header is missing or invalid.");
            }
            uint length = U32(bytes);
            if (length < 132 || length > data.Length) { throw new InvalidDataException("ICC profile extent is invalid."); }
            data = data[..(int)length]; bytes = data.Span;
            if (bytes[8] is not (2 or 4)) { throw new NotSupportedException("ICC conversion supports profile versions 2 and 4."); }
            ReadOnlySpan<byte> deviceClass = bytes.Slice(12, 4);
            if (!deviceClass.SequenceEqual("scnr"u8) && !deviceClass.SequenceEqual("mntr"u8) &&
                !deviceClass.SequenceEqual("prtr"u8) && !deviceClass.SequenceEqual("spac"u8))
            {
                throw new NotSupportedException("ICC conversion requires an input, display, output or color-space profile.");
            }
            if (!bytes.Slice(20, 4).SequenceEqual("XYZ "u8)) { throw new NotSupportedException("ICC matrix conversion requires an XYZ connection space."); }
            if (Math.Abs(IccToneCurve.Fixed(bytes[68..]) - 0.9642) > 1.0 / 65536 ||
                IccToneCurve.Fixed(bytes[72..]) != 1 || Math.Abs(IccToneCurve.Fixed(bytes[76..]) - 0.8249) > 1.0 / 65536)
            {
                throw new InvalidDataException("ICC profile connection-space illuminant must be D50.");
            }
            if (U32(bytes[64..]) > 3) { throw new InvalidDataException("ICC profile rendering intent is invalid."); }
            uint count = U32(bytes[128..]);
            if (count > (data.Length - 132) / 12) { throw new InvalidDataException("ICC tag table is truncated."); }
            int tableEnd = 132 + (int)count * 12;
            List<(uint Offset, uint Size)> ranges = [];
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> entry = bytes.Slice(132 + i * 12, 12);
                uint offset = U32(entry[4..]), size = U32(entry[8..]);
                if (offset < tableEnd || (offset & 3) != 0 || size < 8 || offset > length || size > length - offset)
                {
                    throw new InvalidDataException("ICC tag exceeds the profile or overlaps its table.");
                }
                ranges.Add((offset, size));
                if (!tags.TryAdd(U32(entry), data.Slice((int)offset, (int)size)))
                {
                    throw new InvalidDataException("ICC profile contains a duplicate tag.");
                }
            }
            ranges.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            for (int i = 1; i < ranges.Count; i++)
            {
                (uint Offset, uint Size) previous = ranges[i - 1];
                if (ranges[i].Offset < previous.Offset + previous.Size && ranges[i] != previous)
                {
                    throw new InvalidDataException("ICC tag data overlaps without sharing the same extent.");
                }
            }
            if (HasTag("A2B0"u8) || HasTag("A2B1"u8) || HasTag("A2B2"u8) || HasTag("D2B0"u8) ||
                HasTag("D2B1"u8) || HasTag("D2B2"u8) || HasTag("D2B3"u8))
            {
                throw new NotSupportedException("ICC lookup-table and multi-process transforms are not supported.");
            }
            ReadOnlySpan<byte> space = bytes.Slice(16, 4);
            if (space.SequenceEqual("RGB "u8))
            {
                (double rx, double ry, double rz) = Xyz("rXYZ"u8);
                (double gx, double gy, double gz) = Xyz("gXYZ"u8);
                (double bx, double by, double bz) = Xyz("bXYZ"u8);
                Matrix = new(rx, gx, bx, ry, gy, by, rz, gz, bz);
                double determinant = rx * (gy * bz - gz * by) - gx * (ry * bz - rz * by) + bx * (ry * gz - rz * gy);
                if (determinant == 0) { throw new InvalidDataException("ICC RGB colorant matrix is singular."); }
                ReadOnlyMemory<byte> red = Tag("rTRC"u8), green = Tag("gTRC"u8), blue = Tag("bTRC"u8);
                Red = IccToneCurve.Sample(red.Span, token);
                Green = green.Equals(red) ? Red : IccToneCurve.Sample(green.Span, token);
                Blue = blue.Equals(red) ? Red : blue.Equals(green) ? Green : IccToneCurve.Sample(blue.Span, token);
            }
            else if (space.SequenceEqual("GRAY"u8))
            {
                Gray = true;
                Matrix = new(0.9642, 0, 0, 1, 0, 0, 0.8249, 0, 0);
                Red = IccToneCurve.Sample(Tag("kTRC"u8).Span, token);
                Green = Blue = Red;
            }
            else { throw new NotSupportedException("ICC matrix conversion requires RGB or grayscale samples."); }
        }

        private bool HasTag(ReadOnlySpan<byte> signature) => tags.ContainsKey(U32(signature));

        private ReadOnlyMemory<byte> Tag(ReadOnlySpan<byte> signature) => tags.TryGetValue(U32(signature), out ReadOnlyMemory<byte> value)
            ? value : throw new InvalidDataException("ICC profile is missing a required matrix/TRC tag.");

        private (double X, double Y, double Z) Xyz(ReadOnlySpan<byte> signature)
        {
            ReadOnlySpan<byte> tag = Tag(signature).Span;
            if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8) || U32(tag[4..]) != 0)
            {
                throw new InvalidDataException("ICC matrix colorant is not a valid XYZ value.");
            }
            return (IccToneCurve.Fixed(tag[8..]), IccToneCurve.Fixed(tag[12..]), IccToneCurve.Fixed(tag[16..]));
        }

        private static uint U32(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32BigEndian(value);
    }
}
