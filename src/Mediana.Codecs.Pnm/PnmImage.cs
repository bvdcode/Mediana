// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Pnm
{
    internal record PnmImage(int Kind, int Width, int Height, int Maximum, ReadOnlyMemory<byte> Raster)
    {
        public bool Bitmap => Kind is 1 or 4;
        public int Channels => Kind is 3 or 6 ? 3 : 1;
        public bool Plain => Kind <= 3;
        public string Format => Kind switch
        {
            1 or 4 => "pbm",
            2 or 5 => "pgm",
            3 or 6 => "ppm",
            _ => throw new InvalidDataException("Invalid PNM image type.")
        };

        public static List<PnmImage> Read(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            ReadOnlySpan<byte> data = encoded.Span;
            PnmTokens reader = new(data, token);
            List<PnmImage> images = [];
            while (reader.Position < data.Length)
            {
                token.ThrowIfCancellationRequested();
                int start = reader.Position;
                if (data.Length - start < 3 || data[start] != 'P' || data[start + 1] is < (byte)'1' or > (byte)'6' || !PnmTokens.White(data[start + 2]))
                {
                    throw new InvalidDataException("Invalid PNM magic number.");
                }
                int kind = data[start + 1] - '0';
                reader.Advance(2);
                int width = reader.Number(), height = reader.Number();
                int maximum = kind is 1 or 4 ? 1 : reader.Number();
                if (width == 0 || height == 0 || maximum is < 1 or > 65535) { throw new InvalidDataException("Invalid PNM dimensions or maximum value."); }
                int channels = kind is 3 or 6 ? 3 : 1;
                int rasterStart;
                if (kind >= 4)
                {
                    reader.RasterDelimiter();
                    rasterStart = reader.Position;
                    long rowBytes = kind == 4 ? ((long)width + 7) / 8 : (long)width * channels * (maximum < 256 ? 1 : 2);
                    if (rowBytes > (data.Length - rasterStart) / height) { throw new InvalidDataException("Truncated PNM binary raster."); }
                    reader.Advance((int)(rowBytes * height));
                }
                else
                {
                    rasterStart = reader.Position;
                    long count = (long)width * height * channels;
                    if (count > data.Length - rasterStart) { throw new InvalidDataException("Truncated PNM plain raster."); }
                    for (long i = 0; i < count; i++)
                    {
                        if ((i & 255) == 0) { token.ThrowIfCancellationRequested(); }
                        int value = kind == 1 ? reader.Bit() : reader.Number();
                        if (value > maximum) { throw new InvalidDataException("PNM sample exceeds the maximum value."); }
                    }
                }
                images.Add(new(kind, width, height, maximum, encoded.Slice(rasterStart, reader.Position - rasterStart)));
                if (kind <= 3)
                {
                    reader.SkipSpace();
                    if (reader.Position != data.Length) { throw new InvalidDataException("Plain PNM contains more than one image or excess samples."); }
                }
            }
            if (images.Count == 0) { throw new InvalidDataException("Empty PNM input."); }
            return images;
        }
    }
}
