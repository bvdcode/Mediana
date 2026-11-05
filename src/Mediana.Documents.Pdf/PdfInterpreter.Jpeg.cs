// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private PixelBuffer DecodeJpeg(PdfObject obj, ReadOnlyMemory<byte> data, int width, int height, int bits, PdfValue space, bool stencil, int reduction)
        {
            if (stencil || bits != 8)
            {
                throw new InvalidDataException("PDF DCTDecode requires eight-bit image components.");
            }
            int components = space.Name() switch
            {
                "DeviceGray" or "G" => 1,
                "DeviceRGB" or "RGB" => 3,
                _ => throw new NotSupportedException("PDF JPEG currently requires DeviceGray or DeviceRGB.")
            };
            PdfValue parameters = Get(obj.Value, "DecodeParms");
            if (parameters.Kind == PdfValueKind.Array) { parameters = Resolve(parameters.Items[^1]); }
            if (parameters.Kind is not (PdfValueKind.Null or PdfValueKind.Dictionary))
            {
                throw new InvalidDataException("PDF JPEG decoding parameters require a dictionary.");
            }
            PdfValue transform = Get(parameters, "ColorTransform");
            int configured = transform.Kind == PdfValueKind.Null ? 1 : transform.Integer();
            if (configured is not (0 or 1)) { throw new InvalidDataException("Invalid PDF JPEG ColorTransform."); }
            PdfValue decode = Get(obj.Value, "Decode"), mask = ColorKey(obj);
            double[] endpoints = SampleRanges(decode, components, "Decode");
            double[] ranges = SampleRanges(mask.Kind == PdfValueKind.Array ? mask : PdfValue.Null, components, "Mask");
            long available = storage.Budget.Options.MaximumImageBytes - _imageBytes;
            if (available <= 0) { throw new MediaMemoryLimitException(1, available); }
            MediaBufferAllocator workspace = _imageAllocator.CreateScope(available);
            using JpegImage image = JpegDecoder.DecodeReducedComponents(data, workspace, reduction, new()
            {
                MaximumEncodedBytes = storage.Budget.Options.MaximumInputBytes,
                MaximumPixels = checked(width * height)
            }, token);
            if (image.EncodedWidth != width || image.EncodedHeight != height || image.ComponentCount != components)
            {
                throw new InvalidDataException("PDF JPEG dimensions or component count differ from the image dictionary.");
            }
            foreach (string warning in image.Warnings)
            {
                _warnings.Add(warning);
            }
            JpegColorSpace color;
            if (components == 1) { color = JpegColorSpace.Rgb; }
            else
            {
                color = (image.AdobeColorTransform ?? configured) switch
                {
                    0 => JpegColorSpace.Rgb,
                    1 => JpegColorSpace.YCbCr,
                    2 => throw new NotSupportedException("PDF JPEG YCCK conversion is not implemented."),
                    _ => throw new InvalidDataException("Invalid PDF JPEG Adobe transform.")
                };
            }
            PixelBuffer pixels = new(image.Width, image.Height, _imageAllocator);
            bool transferred = false;
            try
            {
                image.WriteRgbaTo(pixels.Pixels.Span, color, token);
                RemapJpeg(pixels, components, endpoints, ranges);
                transferred = true;
                return pixels;
            }
            finally { if (!transferred) { pixels.Dispose(); } }
        }

        private double[] SampleRanges(PdfValue value, int components, string name)
        {
            if (value.Kind == PdfValueKind.Null) { return []; }
            if (value.Kind != PdfValueKind.Array || value.Items.Length != components * 2)
            {
                throw new InvalidDataException("Invalid PDF JPEG " + name + " array.");
            }
            double[] ranges = new double[components * 2];
            for (int i = 0; i < ranges.Length; i++)
            {
                PdfValue item = Resolve(value.Items[i]);
                if (name == "Mask")
                {
                    int sample = item.Integer();
                    if (sample is < 0 or > 255) { throw new InvalidDataException("Invalid PDF JPEG color-key range."); }
                    ranges[i] = sample;
                }
                else { ranges[i] = item.Numeric(); }
            }
            return ranges;
        }

        private void RemapJpeg(PixelBuffer pixels, int components, double[] endpoints, double[] mask)
        {
            if (endpoints.Length == 0 && mask.Length == 0) { return; }
            Span<byte> rgba = pixels.Pixels.Span;
            for (int y = 0; y < pixels.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < pixels.Width; x++)
                {
                    Span<byte> pixel = rgba.Slice((y * pixels.Width + x) * 4, 4);
                    bool transparent = mask.Length != 0;
                    for (int c = 0; c < components; c++)
                    {
                        int sample = pixel[c];
                        if (transparent && (sample < mask[c * 2] || sample > mask[c * 2 + 1])) { transparent = false; }
                        if (endpoints.Length != 0) { pixel[c] = Channel(endpoints[c * 2] + sample * (endpoints[c * 2 + 1] - endpoints[c * 2]) / 255); }
                    }
                    if (components == 1) { pixel[1] = pixel[0]; pixel[2] = pixel[0]; }
                    if (transparent) { pixel[3] = 0; }
                }
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
