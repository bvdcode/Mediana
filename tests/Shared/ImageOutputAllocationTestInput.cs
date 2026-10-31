// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class ImageOutputAllocationTestInput
    {
        public static async Task VerifyAsync()
        {
            const int Width = 1000;
            const int Height = 500;
            const int PixelBytes = Width * Height * 4;
            const int Repetitions = 3;
            byte[] svg = "<svg width='1000' height='500'/>"u8.ToArray();
            byte[] tgs = "{\"w\":1000,\"h\":500,\"fr\":4,\"ip\":0,\"op\":1,\"layers\":[]}"u8.ToArray();
            byte[] pdf = PdfTestDocument.Create("");
            (IImageDecoder Decoder, byte[] Input)[] cases =
            [
                (new SvgImageDecoder(renderOptions: new() { Supersampling = 1 }), svg),
                (new TgsImageDecoder(renderOptions: new() { Supersampling = 1 }), tgs),
                (new PdfImageDecoder(renderOptions: new() { Dpi = 360, Supersampling = 1 }), pdf)
            ];
            foreach ((IImageDecoder decoder, byte[] input) in cases)
            {
                MediaBufferAllocator allocator = new(PixelBytes);
                for (int warmup = 0; warmup < Repetitions; warmup++)
                {
                    using VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator);
                }
                long before = GC.GetTotalAllocatedBytes(precise: true);
                for (int iteration = 0; iteration < Repetitions; iteration++)
                {
                    using VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator);
                    using VideoFrame srgb = SrgbConverter.Apply(frame, allocator);
                    if (frame.Format.Width != Width || frame.Format.Height != Height || frame.Data.Length != PixelBytes)
                    {
                        throw new InvalidDataException("Allocation verification requires the complete requested image.");
                    }
                    if (!srgb.Data.Equals(frame.Data))
                    {
                        throw new InvalidDataException("Already-sRGB image preparation must retain its existing pixel storage.");
                    }
                }
                long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
                long maximum = (long)Repetitions * (PixelBytes + PixelBytes / 2);
                if (allocated >= maximum || allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException($"{decoder.ImplementationId} allocated {allocated} bytes for {Repetitions} images; "
                        + $"the {maximum}-byte limit excludes a duplicate RGBA output and all leases must be released.");
                }
            }
        }
    }
}
