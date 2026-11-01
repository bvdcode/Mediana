// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Drawing;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private readonly HashSet<PdfObject> _activeImages = [];
        private PdfImage Image(PdfObject obj, string name, int depth)
        {
            if (Get(obj.Value, "ImageMask").Number == 0 && _decodedImages.TryGetValue(obj, out PdfImage? existing)) { return existing; }
            if (depth >= storage.Budget.Options.MaximumDepth || !_activeImages.Add(obj)) { throw new InvalidDataException("Cyclic or excessive PDF image mask."); }
            PixelBuffer? pending = null;
            try
            {
                int width = Get(obj.Value, "Width").Integer(), height = Get(obj.Value, "Height").Integer();
                if (width <= 0 || height <= 0 || (long)width * height > storage.Budget.Options.MaximumImagePixels) { throw new InvalidDataException("PDF image exceeds its pixel limit."); }
                _imageBytes += (long)width * height * 4;
                if (_imageBytes > storage.Budget.Options.MaximumImageBytes) { throw new InvalidDataException("PDF page images exceed their byte limit."); }
                PdfValue filters = Get(obj.Value, "Filter");
                PdfValue terminal = filters.Kind == PdfValueKind.Array ? Resolve(filters.Items[^1]) : filters;
                string codec = terminal.Kind == PdfValueKind.Null ? string.Empty : terminal.Name();
                ReadOnlyMemory<byte> data = storage.Decode(obj, token, image: true);
                PdfValue colorSpace = Get(obj.Value, "ColorSpace");
                bool stencil = Get(obj.Value, "ImageMask").Number != 0;
                int bits = stencil ? 1 : Get(obj.Value, "BitsPerComponent").Kind == PdfValueKind.Null && codec == "JPXDecode" ? 8 : Get(obj.Value, "BitsPerComponent").Integer();
                PixelBuffer pixels;
                if (codec == "DCTDecode")
                {
                    pixels = DecodeJpeg(obj, data, width, height, bits, colorSpace, stencil);
                }
                else if (codec == "JPXDecode" && colorSpace.Kind == PdfValueKind.Null)
                {
                    if (obj.Value.Get("Decode").Kind != PdfValueKind.Null || Get(obj.Value, "SMaskInData").Number != 0) { throw new NotSupportedException("JPX Decode and SMaskInData overrides are not supported yet."); }
                    Jpeg2000Image jpeg = Jpeg2000Decoder.Decode(data, new() { MaximumEncodedBytes = storage.Budget.Options.MaximumInputBytes, MaximumPixels = storage.Budget.Options.MaximumImagePixels, MaximumWorkingBytes = storage.Budget.Options.MaximumImageBytes, ExpectedWidth = width, ExpectedHeight = height }, token);
                    if (jpeg.Width != width || jpeg.Height != height) { throw new InvalidDataException("PDF JPEG 2000 dimensions differ."); }
                    pixels = new(width, height);
                    bool converted = false;
                    try
                    {
                        jpeg.WriteRgbaTo(pixels.Pixels.Span, cancellationToken: token);
                        converted = true;
                    }
                    finally
                    {
                        if (!converted) { pixels.Dispose(); }
                    }
                }
                else
                {
                    if (codec == "JPXDecode") { throw new NotSupportedException("JPX images with an explicit PDF color space are not implemented yet."); }
                    if (codec == "JBIG2Decode")
                    {
                        PdfValue parameters = Get(obj.Value, "DecodeParms");
                        if (parameters.Kind == PdfValueKind.Array) { parameters = Resolve(parameters.Items[^1]); }
                        PdfValue global = parameters.Get("JBIG2Globals");
                        ReadOnlyMemory<byte> globals = global.Kind == PdfValueKind.Null ? ReadOnlyMemory<byte>.Empty : storage.Decode(storage.Object(global, token), token);
                        Jbig2Document jbig = Jbig2Decoder.Decode(data, globals, new() { MaximumEncodedBytes = storage.Budget.Options.MaximumInputBytes, MaximumPixels = storage.Budget.Options.MaximumImagePixels, MaximumPages = 1, MaximumWorkingBytes = storage.Budget.Options.MaximumImageBytes, ExpectedWidth = width, ExpectedHeight = height }, token);
                        if (jbig.Pages.Count != 1) { throw new InvalidDataException("PDF JBIG2 image requires one page."); }
                        byte[] bitmap = jbig.Pages[0].Data.ToArray();
                        for (int i = 0; i < bitmap.Length; i++) { bitmap[i] ^= 255; }
                        data = bitmap;
                        bits = 1;
                    }
                    if (bits is not (1 or 2 or 4 or 8 or 16)) { throw new NotSupportedException("Unsupported PDF image component depth."); }
                    pixels = Samples(data.Span, width, height, bits, colorSpace, Get(obj.Value, "Decode"), ColorKey(obj), stencil);
                }
                pending = pixels;
                PdfValue soft = obj.Value.Get("SMask");
                PdfValue mask = obj.Value.Get("Mask");
                PdfValue maskReference = PdfValue.Null;
                if (soft.Kind != PdfValueKind.Null) { maskReference = soft; }
                else if (mask.Kind == PdfValueKind.Reference && Resolve(mask).Kind != PdfValueKind.Array) { maskReference = mask; }
                if (maskReference.Kind != PdfValueKind.Null)
                {
                    PdfObject maskObject = storage.Object(maskReference, token);
                    if (soft.Kind != PdfValueKind.Null && maskObject.Value.Get("Matte").Kind != PdfValueKind.Null) { throw new NotSupportedException("Preblended PDF soft masks are not supported yet."); }
                    PdfImage alpha = Image(maskObject, name + "/mask", depth + 1);
                    for (int y = 0; y < height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int x = 0; x < width; x++)
                        {
                            int offset = (y * width + x) * 4;
                            int source = ((y * alpha.Height / height) * alpha.Width + x * alpha.Width / width) * 4;
                            byte opacity = soft.Kind != PdfValueKind.Null ? alpha.Pixels.Span[source] : alpha.Pixels.Span[source + 3];
                            pixels.Pixels.Span[offset + 3] = (byte)((pixels.Pixels.Span[offset + 3] * opacity + 127) / 255);
                        }
                    }
                }
                PdfImage result = new(name, pixels);
                if (!stencil) { _decodedImages.Add(obj, result); }
                _images.Add(result); pending = null; return result;
            }
            finally { pending?.Dispose(); _activeImages.Remove(obj); }
        }

        private PdfValue ColorKey(PdfObject obj)
        {
            if (obj.Value.Get("SMask").Kind != PdfValueKind.Null) { return PdfValue.Null; }
            PdfValue declaration = obj.Value.Get("Mask");
            PdfValue mask = Resolve(declaration);
            return mask.Kind switch
            {
                PdfValueKind.Null or PdfValueKind.Array => mask,
                PdfValueKind.Dictionary when declaration.Kind == PdfValueKind.Reference => PdfValue.Null,
                _ => throw new InvalidDataException("A PDF image Mask requires a sample range array or an image stream reference.")
            };
        }

        private PixelBuffer Samples(ReadOnlySpan<byte> samples, int width, int height, int bits, PdfValue space, PdfValue decode, PdfValue mask, bool stencil)
        {
            bool indexed = space.Kind == PdfValueKind.Array && Resolve(space.Items[0]).Text == "Indexed";
            PdfValue paletteSpace = space;
            ReadOnlyMemory<byte> palette = ReadOnlyMemory<byte>.Empty;
            int highest = 0;
            if (indexed)
            {
                if (space.Items.Length != 4) { throw new InvalidDataException("Invalid PDF indexed color space."); }
                paletteSpace = Resolve(space.Items[1]); highest = Resolve(space.Items[2]).Integer();
                PdfValue lookup = space.Items[3];
                palette = lookup.Kind == PdfValueKind.Reference ? storage.Decode(storage.Object(lookup, token), token) : Resolve(lookup).Bytes;
                if (highest is < 0 or > 255) { throw new InvalidDataException("Invalid PDF palette size."); }
            }
            string color = stencil ? "DeviceGray" : paletteSpace.Name();
            int components = color switch { "DeviceGray" or "G" => 1, "DeviceRGB" or "RGB" => 3, "DeviceCMYK" or "CMYK" => 4, _ => throw new NotSupportedException("Unsupported PDF color space: " + color) };
            if (indexed && palette.Length < (highest + 1) * components) { throw new InvalidDataException("Truncated PDF palette."); }
            int channels = indexed || stencil ? 1 : components;
            int row = checked((int)(((long)width * channels * bits + 7) / 8));
            if ((long)row * height != samples.Length) { throw new InvalidDataException("PDF image sample length mismatch."); }
            if (decode.Kind != PdfValueKind.Null && decode.Items.Length != channels * 2) { throw new InvalidDataException("Invalid PDF image Decode array."); }
            if (mask.Kind == PdfValueKind.Array && mask.Items.Length != channels * 2) { throw new InvalidDataException("Invalid PDF color-key mask."); }
            int maximum = (1 << bits) - 1;
            PixelBuffer output = new(width, height);
            Span<double> values = stackalloc double[4];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    bool transparent = mask.Kind == PdfValueKind.Array;
                    for (int c = 0; c < channels; c++)
                    {
                        int bit = (x * channels + c) * bits, sample = 0;
                        for (int k = 0; k < bits; k++) { int index = bit + k; sample = (sample << 1) | ((samples[y * row + index / 8] >> (7 - index % 8)) & 1); }
                        if (transparent && (sample < mask.Items[c * 2].Numeric() || sample > mask.Items[c * 2 + 1].Numeric())) { transparent = false; }
                        double low = decode.Kind == PdfValueKind.Null ? 0 : decode.Items[c * 2].Numeric();
                        double high = decode.Kind == PdfValueKind.Null ? indexed ? maximum : 1 : decode.Items[c * 2 + 1].Numeric();
                        values[c] = low + sample * (high - low) / maximum;
                    }
                    Rgba32 result;
                    if (stencil) { result = _state.Fill with { A = Channel(1 - values[0]) }; }
                    else
                    {
                        if (indexed)
                        {
                            int index = Math.Clamp((int)Math.Round(values[0]), 0, highest);
                            for (int c = 0; c < components; c++) { values[c] = palette.Span[index * components + c] / 255.0; }
                        }
                        result = components switch
                        {
                            1 => new(Channel(values[0]), Channel(values[0]), Channel(values[0])),
                            3 => new(Channel(values[0]), Channel(values[1]), Channel(values[2])),
                            4 => new(Channel(1 - Math.Min(1, values[0] + values[3])), Channel(1 - Math.Min(1, values[1] + values[3])), Channel(1 - Math.Min(1, values[2] + values[3]))),
                            _ => throw new InvalidDataException("Invalid PDF component count.")
                        };
                    }
                    output[x, y] = transparent ? result with { A = 0 } : result;
                }
            }
            return output;
        }
    }
}
