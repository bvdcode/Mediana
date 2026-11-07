// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Png
{
    internal static class PngScanline
    {
        public static void Unfilter(int filter, Span<byte> row, ReadOnlySpan<byte> previous, int stride, CancellationToken token)
        {
            if (filter is < 0 or > 4)
            {
                throw new InvalidDataException("Invalid PNG scanline filter.");
            }
            if (filter == 0)
            {
                token.ThrowIfCancellationRequested();
                return;
            }
            for (int index = 0; index < row.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int left = index >= stride ? row[index - stride] : 0;
                int above = previous[index];
                int corner = index >= stride ? previous[index - stride] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, corner),
                    _ => throw new InvalidDataException("Invalid PNG scanline filter.")
                };
                row[index] = unchecked((byte)(row[index] + predictor));
            }
        }

        public static void Expand(PngImageData image, ReadOnlySpan<byte> row, Span<byte> output, int offset, int width, int step,
            CancellationToken token)
        {
            if (image.ColorType == 6 && image.Depth == 8 && step == 1)
            {
                row.CopyTo(output[offset..]);
                return;
            }
            int bit = 0;
            int maximum = (1 << image.Depth) - 1;
            for (int x = 0; x < width; x++)
            {
                if ((x & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                Span<byte> pixel = output.Slice(offset + x * step * 4, 4);
                int first = ReadSample(row, image.Depth, ref bit);
                switch (image.ColorType)
                {
                    case 0:
                        pixel[0] = pixel[1] = pixel[2] = Scale(first, maximum);
                        pixel[3] = !image.Transparency.IsEmpty && first == BinaryPrimitives.ReadUInt16BigEndian(image.Transparency.Span)
                            ? (byte)0 : (byte)255;
                        break;
                    case 2:
                        int green = ReadSample(row, image.Depth, ref bit);
                        int blue = ReadSample(row, image.Depth, ref bit);
                        pixel[0] = Scale(first, maximum);
                        pixel[1] = Scale(green, maximum);
                        pixel[2] = Scale(blue, maximum);
                        ReadOnlySpan<byte> transparent = image.Transparency.Span;
                        pixel[3] = !transparent.IsEmpty && first == BinaryPrimitives.ReadUInt16BigEndian(transparent) &&
                            green == BinaryPrimitives.ReadUInt16BigEndian(transparent[2..]) && blue == BinaryPrimitives.ReadUInt16BigEndian(transparent[4..])
                            ? (byte)0 : (byte)255;
                        break;
                    case 3:
                        if (first >= image.Palette.Length / 3)
                        {
                            throw new InvalidDataException("PNG pixel refers to a missing palette entry.");
                        }
                        image.Palette.Span.Slice(first * 3, 3).CopyTo(pixel);
                        pixel[3] = first < image.Transparency.Length ? image.Transparency.Span[first] : (byte)255;
                        break;
                    case 4:
                        pixel[0] = pixel[1] = pixel[2] = Scale(first, maximum);
                        pixel[3] = Scale(ReadSample(row, image.Depth, ref bit), maximum);
                        break;
                    case 6:
                        pixel[0] = Scale(first, maximum);
                        pixel[1] = Scale(ReadSample(row, image.Depth, ref bit), maximum);
                        pixel[2] = Scale(ReadSample(row, image.Depth, ref bit), maximum);
                        pixel[3] = Scale(ReadSample(row, image.Depth, ref bit), maximum);
                        break;
                    default:
                        throw new InvalidDataException("Invalid PNG color type.");
                }
            }
        }

        private static byte Scale(int value, int maximum) => (byte)((value * 255 + maximum / 2) / maximum);

        private static int ReadSample(ReadOnlySpan<byte> row, int depth, ref int bit)
        {
            int value = depth switch
            {
                1 or 2 or 4 => (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1),
                8 => row[bit >> 3],
                16 => BinaryPrimitives.ReadUInt16BigEndian(row[(bit >> 3)..]),
                _ => throw new InvalidDataException("Invalid PNG bit depth.")
            };
            bit += depth;
            return value;
        }

        private static int Paeth(int left, int above, int corner)
        {
            int p = left + above - corner;
            int a = Math.Abs(p - left);
            int b = Math.Abs(p - above);
            int c = Math.Abs(p - corner);
            if (a <= b && a <= c)
            {
                return left;
            }
            return b <= c ? above : corner;
        }
    }
}
