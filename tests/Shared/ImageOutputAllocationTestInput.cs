// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Images.Heif;
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
        public static async Task VerifyHeicInputAsync(string path)
        {
            const int PaddingBytes = 2 * 1024 * 1024;
            const int Repetitions = 3;
            byte[] source = await File.ReadAllBytesAsync(path);
            byte[] input = new byte[source.Length + PaddingBytes];
            source.CopyTo(input, 0);
            BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(source.Length), PaddingBytes);
            "free"u8.CopyTo(input.AsSpan(source.Length + 4));
            HeicImageDecoder decoder = new(SampleAspectRatio.Square);
            MediaBufferAllocator allocator = new(64 * 48 * 4);
            for (int warmup = 0; warmup < Repetitions; warmup++)
            {
                using VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator);
            }
            long before = GC.GetTotalAllocatedBytes(precise: true);
            for (int iteration = 0; iteration < Repetitions; iteration++)
            {
                using VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator);
                if (frame.Format.Width != 64 || frame.Format.Height != 48)
                {
                    throw new InvalidDataException("HEIC allocation verification requires the complete source image.");
                }
            }
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            if (allocated >= (long)Repetitions * PaddingBytes / 2 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Registered HEIC decoding must borrow encoded input and release its output budget.");
            }
        }

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
