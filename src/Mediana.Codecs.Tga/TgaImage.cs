// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Tga
{
    internal class TgaImage
    {
        public TgaImage(ReadOnlyMemory<byte> encoded)
        {
            ReadOnlySpan<byte> data = encoded.Span;
            if (!Recognize(data)) { throw new InvalidDataException("Invalid TGA header."); }
            Type = data[2]; Depth = data[16]; Descriptor = data[17];
            Width = Word(data, 12); Height = Word(data, 14);
            PaletteStart = Word(data, 3); PaletteCount = Word(data, 5); PaletteDepth = data[7];
            int offset = 18 + data[0], paletteBytes = data[1] * PaletteCount * ((PaletteDepth + 7) / 8);
            if (offset > data.Length || paletteBytes > data.Length - offset) { throw new InvalidDataException("Truncated TGA palette or image ID."); }
            Palette = encoded.Slice(offset, paletteBytes);
            offset += paletteBytes;
            int end = data.Length;
            AlphaType = (Descriptor & 15) == 0 ? 0 : 3;
            List<MediaProperty> properties = [new("tga", "image_type", Type), new("tga", "bit_depth", Depth),
                new("tga", "descriptor", Descriptor), new("tga", "origin", Word(data, 8), Word(data, 10)),
                new("tga", "palette_entries", PaletteCount)];
            if (data[0] != 0) { properties.Add(new("tga", "image_id", MediaPropertyValue.FromBytes(data.Slice(18, data[0])))); }
            if (data.Length >= 26 && data[^18..].SequenceEqual("TRUEVISION-XFILE.\0"u8))
            {
                end -= 26;
                uint extension = BinaryPrimitives.ReadUInt32LittleEndian(data[end..]);
                uint developer = BinaryPrimitives.ReadUInt32LittleEndian(data[(end + 4)..]);
                if (developer != 0) { end = Bound(developer, offset, end); }
                if (extension != 0)
                {
                    int start = Bound(extension, offset, data.Length - 26);
                    if (data.Length - 26 - start < 495 || Word(data, start) != 495) { throw new InvalidDataException("Invalid TGA extension area."); }
                    ReadOnlySpan<byte> area = data.Slice(start, 495);
                    AlphaType = area[494];
                    if (AlphaType > 4) { throw new InvalidDataException("Invalid TGA alpha interpretation."); }
                    int numerator = Word(area, 474), denominator = Word(area, 476);
                    if (denominator != 0 && numerator != 0) { Aspect = new(numerator, denominator); }
                    GammaNumerator = Word(area, 478); GammaDenominator = Word(area, 480);
                    properties.Add(new("tga", "gamma_fraction", GammaNumerator, GammaDenominator));
                    properties.Add(new("tga", "alpha_type", AlphaType));
                    end = Math.Min(end, start);
                    for (int field = 482; field <= 490; field += 4)
                    {
                        uint location = BinaryPrimitives.ReadUInt32LittleEndian(area[field..]);
                        if (location != 0) { end = Math.Min(end, Bound(location, offset, data.Length - 26)); }
                    }
                    HasColorCorrection = BinaryPrimitives.ReadUInt32LittleEndian(area[482..]) != 0;
                }
            }
            if (end < offset) { throw new InvalidDataException("TGA metadata overlaps its palette or header."); }
            Raster = encoded.Slice(offset, end - offset);
            Metadata = new("image", properties);
        }

        public static bool Recognize(ReadOnlySpan<byte> data)
        {
            if (data.Length < 18 || data[1] > 1 || data[2] is not (1 or 2 or 3 or 9 or 10 or 11) ||
                Word(data, 12) == 0 || Word(data, 14) == 0 || (data[17] >> 6) == 3) { return false; }
            bool indexed = data[2] is 1 or 9, gray = data[2] is 3 or 11;
            if (indexed && (data[1] != 1 || Word(data, 5) == 0 || data[16] is not (8 or 16))) { return false; }
            if (data[1] == 1 && data[7] is not (15 or 16 or 24 or 32)) { return false; }
            if (!indexed && (gray ? data[16] is not (8 or 16) : data[16] is not (15 or 16 or 24 or 32))) { return false; }
            int colorDepth = indexed ? data[7] : data[16];
            int alphaBits = data[17] & 15;
            return alphaBits == 0 || alphaBits == 1 && colorDepth == 16 || alphaBits == 8 && (colorDepth == 32 || gray && colorDepth == 16);
        }

        private static int Bound(uint offset, int minimum, int end)
        {
            if (offset < minimum || offset >= end) { throw new InvalidDataException("TGA metadata offset exceeds its file."); }
            return (int)offset;
        }
        private static int Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        public int Type { get; }
        public int Depth { get; }
        public int Descriptor { get; }
        public int Width { get; }
        public int Height { get; }
        public int PaletteStart { get; }
        public int PaletteCount { get; }
        public int PaletteDepth { get; }
        public int AlphaType { get; }
        public int GammaNumerator { get; }
        public int GammaDenominator { get; }
        public bool HasColorCorrection { get; }
        public SampleAspectRatio? Aspect { get; }
        public ReadOnlyMemory<byte> Palette { get; }
        public ReadOnlyMemory<byte> Raster { get; }
        public MediaMetadata Metadata { get; }
    }
}
