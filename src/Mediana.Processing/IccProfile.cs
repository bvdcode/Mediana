// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Processing
{
    internal class IccProfile
    {
        private readonly Dictionary<uint, ReadOnlyMemory<byte>> tags = [];
        internal uint ColorSpace { get; }
        internal uint ConnectionSpace { get; }

        internal IccProfile(ReadOnlyMemory<byte> data, CancellationToken token)
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
            ColorSpace = U32(bytes[16..]);
            ConnectionSpace = U32(bytes[20..]);
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
        }

        internal bool HasTag(ReadOnlySpan<byte> signature) => tags.ContainsKey(U32(signature));
        internal bool TryTag(ReadOnlySpan<byte> signature, out ReadOnlyMemory<byte> value) => tags.TryGetValue(U32(signature), out value);
        internal ReadOnlyMemory<byte> Tag(ReadOnlySpan<byte> signature) => TryTag(signature, out ReadOnlyMemory<byte> value)
            ? value : throw new InvalidDataException("ICC profile is missing a required transform tag.");
        internal static uint U32(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32BigEndian(value);
    }
}
