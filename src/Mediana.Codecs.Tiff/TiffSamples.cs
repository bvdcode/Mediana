// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Tiff;

namespace Mediana.Codecs.Tiff
{
    internal class TiffSamples
    {
        internal readonly int Width, Height, Depth, Channels, Photometric, Planar, Predictor, Orientation, Alpha;
        internal readonly ulong[] Palette;
        internal readonly bool LittleEndian;
        internal TiffSamples(TiffDirectory directory, bool littleEndian)
        {
            LittleEndian = littleEndian;
            Width = checked((int)directory.Number(256)); Height = checked((int)directory.Number(257));
            Channels = checked((int)directory.Number(277, 1)); Photometric = checked((int)directory.Number(262));
            Planar = checked((int)directory.Number(284, 1)); Predictor = checked((int)directory.Number(317, 1));
            Orientation = checked((int)directory.Number(274, 1));
            ulong[] depths = directory.Numbers(258);
            Depth = depths.Length == 0 ? 1 : checked((int)depths[0]);
            if (Width <= 0 || Height <= 0 || Channels <= 0 || Orientation is < 1 or > 8)
            { throw new InvalidDataException("Invalid TIFF sample dimensions or orientation."); }
            if (depths.Any(value => value != (ulong)Depth) || Depth is not (1 or 2 or 4 or 8 or 16)
                || directory.Numbers(339).Any(value => value != 1))
            { throw new NotSupportedException("TIFF decoding requires uniform unsigned 1/2/4/8/16-bit samples."); }
            int colors = Photometric switch
            {
                0 or 1 or 3 => 1,
                2 or 6 => 3,
                5 => 4,
                _ => throw new NotSupportedException($"TIFF photometric interpretation {Photometric} is not supported for pixel decoding.")
            };
            ulong[] extra = directory.Numbers(338);
            if (Channels != colors && (Channels != colors + 1 || extra.Length != 1 || extra[0] is not (1 or 2)))
            { throw new NotSupportedException("TIFF extra samples require one declared associated or straight alpha component."); }
            Alpha = Channels == colors ? 0 : (int)extra[0];
            if (Planar is not (1 or 2) || Predictor is not (1 or 2) || (Predictor == 2 && Depth < 8)
                || (Photometric == 5 && (Planar != 1 || Channels != 4 || Depth != 8 || directory.Number(332, 1) != 1)))
            { throw new NotSupportedException("Unsupported TIFF plane, predictor or separated-ink layout."); }
            Palette = directory.Numbers(320);
            if (Photometric == 3 && Palette.Length != 3 * (1 << Depth)) { throw new InvalidDataException("Invalid TIFF color map."); }
        }
        internal int RowBytes(int width, int channels) => checked((int)(((long)width * channels * Depth + 7) / 8));
        internal void Predict(Span<byte> block, int rowBytes, int channels)
        {
            if (Predictor == 1) { return; }
            for (int start = 0; start < block.Length; start += rowBytes)
            {
                Span<byte> row = block.Slice(start, rowBytes);
                if (Depth == 8) { for (int i = channels; i < row.Length; i++) { row[i] = unchecked((byte)(row[i] + row[i - channels])); } }
                else
                {
                    for (int i = channels * 2; i < row.Length; i += 2)
                    {
                        ushort value = unchecked((ushort)(Read16(row[i..]) + Read16(row[(i - channels * 2)..])));
                        if (LittleEndian) { BinaryPrimitives.WriteUInt16LittleEndian(row[i..], value); }
                        else { BinaryPrimitives.WriteUInt16BigEndian(row[i..], value); }
                    }
                }
            }
        }
        internal void Paint(ReadOnlySpan<byte> row, Span<byte> pixels, int y, int xStart, int width, int plane)
        {
            int components = Planar == 2 ? 1 : Channels;
            Span<int> values = stackalloc int[5];
            for (int x = 0; x < width; x++)
            {
                for (int channel = 0; channel < components; channel++)
                {
                    int index = x * components + channel;
                    int sample = Depth switch
                    {
                        16 => Read16(row[(index * 2)..]),
                        8 => row[index],
                        1 or 2 or 4 => (row[index * Depth / 8] >> (8 - Depth - index * Depth % 8)) & ((1 << Depth) - 1),
                        _ => throw new InvalidDataException("Unsupported TIFF sample precision.")
                    };
                    values[channel] = sample;
                }
                (int targetX, int targetY) = Position(xStart + x, y);
                int outputWidth = Orientation >= 5 ? Height : Width;
                Span<byte> target = pixels.Slice((targetY * outputWidth + targetX) * 4, 4);
                if (Photometric == 3 && (Planar == 1 || plane == 0))
                {
                    int count = 1 << Depth, index = values[0];
                    for (int c = 0; c < 3; c++) { target[c] = (byte)(Palette[index + c * count] >> 8); }
                }
                else if (Photometric == 3 && Planar == 2)
                { target[3] = (byte)(((long)values[0] * 255 + ((1 << Depth) - 1) / 2) / ((1 << Depth) - 1)); }
                else if (Photometric == 5)
                {
                    for (int c = 0; c < 3; c++) { target[c] = (byte)(((255 - values[c]) * (255 - values[3]) + 127) / 255); }
                }
                else
                {
                    for (int c = 0; c < components; c++)
                    {
                        int channel = Planar == 2 ? plane : c;
                        byte value = (byte)(((long)values[c] * 255 + ((1 << Depth) - 1) / 2) / ((1 << Depth) - 1));
                        if (channel == Channels - 1 && Alpha != 0) { target[3] = value; }
                        else if (Photometric is 0 or 1)
                        { target[0] = target[1] = target[2] = Photometric == 0 ? (byte)(255 - value) : value; }
                        else { target[channel] = value; }
                    }
                }
                if (Photometric == 3 && Alpha != 0 && Planar == 1) { target[3] = (byte)(((long)values[1] * 255 + ((1 << Depth) - 1) / 2) / ((1 << Depth) - 1)); }
            }
        }
        internal (int X, int Y) Position(int x, int y) => Orientation switch
        {
            1 => (x, y), 2 => (Width - x - 1, y), 3 => (Width - x - 1, Height - y - 1), 4 => (x, Height - y - 1),
            5 => (y, x), 6 => (Height - y - 1, x), 7 => (Height - y - 1, Width - x - 1), 8 => (y, Width - x - 1),
            _ => throw new InvalidDataException("Invalid TIFF orientation.")
        };
        private ushort Read16(ReadOnlySpan<byte> value) => LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt16BigEndian(value);
    }
}
