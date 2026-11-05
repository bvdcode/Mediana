// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg2000;
using Mediana.Core.Memory;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private PixelBuffer DecodeJpeg2000(PdfObject obj, ReadOnlyMemory<byte> data, int width, int height,
            PdfValue space, bool stencil)
        {
            if (stencil) { throw new NotSupportedException("JPEG 2000 stencil images are not supported."); }
            if (Get(obj.Value, "SMaskInData").Number != 0)
            {
                throw new NotSupportedException("JPEG 2000 embedded PDF opacity overrides are not supported.");
            }
            long available = storage.Budget.Options.MaximumImageBytes - _imageBytes;
            if (available <= 0) { throw new MediaMemoryLimitException(1, available); }
            Jpeg2000Image image = Jpeg2000Decoder.Decode(data, new()
            {
                MaximumEncodedBytes = storage.Budget.Options.MaximumInputBytes,
                MaximumPixels = storage.Budget.Options.MaximumImagePixels,
                MaximumWorkingBytes = available,
                ExpectedWidth = width,
                ExpectedHeight = height
            }, token);
            if (image.Width != width || image.Height != height)
            {
                throw new InvalidDataException("PDF JPEG 2000 dimensions differ from the image dictionary.");
            }
            PixelBuffer pixels = new(width, height, _imageAllocator);
            bool completed = false;
            try
            {
                if (space.Kind == PdfValueKind.Null) { image.WriteRgbaTo(pixels.Pixels.Span, cancellationToken: token); }
                else { Jpeg2000Samples(image, pixels, space, ColorKey(obj)); }
                completed = true;
                return pixels;
            }
            finally { if (!completed) { pixels.Dispose(); } }
        }

        private void Jpeg2000Samples(Jpeg2000Image image, PixelBuffer output, PdfValue space, PdfValue mask)
        {
            int components = space.Name() switch
            {
                "DeviceGray" or "G" => 1,
                "DeviceRGB" or "RGB" => 3,
                "DeviceCMYK" or "CMYK" => 4,
                _ => throw new NotSupportedException("Unsupported PDF JPEG 2000 color space: " + space.Name())
            };
            int expected = components + (image.AlphaComponent >= 0 ? 1 : 0);
            if (image.ComponentCount != expected)
            {
                throw new InvalidDataException("PDF JPEG 2000 color space and component count differ.");
            }
            if (mask.Kind == PdfValueKind.Array && mask.Items.Length != components * 2)
            {
                throw new InvalidDataException("Invalid PDF JPEG 2000 color-key mask.");
            }
            Span<double> values = stackalloc double[4];
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    bool transparent = mask.Kind == PdfValueKind.Array;
                    int color = 0;
                    for (int component = 0; component < image.ComponentCount; component++)
                    {
                        if (component == image.AlphaComponent) { continue; }
                        Jpeg2000Component channel = image.GetComponent(component);
                        long sample = image.GetSample(x, y, component);
                        if (channel.IsSigned) { sample += 1L << (channel.BitDepth - 1); }
                        if (transparent && (sample < Resolve(mask.Items[color * 2]).Numeric()
                            || sample > Resolve(mask.Items[color * 2 + 1]).Numeric())) { transparent = false; }
                        values[color++] = sample / ((1L << channel.BitDepth) - 1.0);
                    }
                    Rgba32 pixel = components switch
                    {
                        1 => new(Channel(values[0]), Channel(values[0]), Channel(values[0])),
                        3 => new(Channel(values[0]), Channel(values[1]), Channel(values[2])),
                        4 => new(Channel(1 - Math.Min(1, values[0] + values[3])), Channel(1 - Math.Min(1, values[1] + values[3])), Channel(1 - Math.Min(1, values[2] + values[3]))),
                        _ => throw new InvalidDataException("Invalid PDF JPEG 2000 component count.")
                    };
                    output[x, y] = transparent ? pixel with { A = 0 } : pixel;
                }
            }
        }
    }
}
