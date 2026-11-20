// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;

namespace Mediana.Codecs.Bmp
{
    internal static class BitmapImageDecoders
    {
        public static ImageDecodeContext Default { get; } = new([
            new BmpImageDecoder(), new IcoImageDecoder(), new AniImageDecoder(),
            new PngImageDecoder(new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }),
            new JpegImageDecoder(new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength })]);
    }
}
