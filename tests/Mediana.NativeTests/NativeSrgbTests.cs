using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeSrgbTests
    {
        public static async Task RunAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-weighted.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame decoded = await session.ReadFrameAsync()
                ?? throw new InvalidDataException("The sRGB source frame is missing.");
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.frame0.srgb.rgba"));
            MediaBufferAllocator allocator = new(128 * 1024);
            using (VideoFrame converted = SrgbConverter.Apply(decoded, allocator))
            {
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb,
                    ColorRange.Full, ChromaLocation.Unspecified);
                if (converted.Data.Length != expected.Length || converted.Format.Color != color || converted.Timestamp != decoded.Timestamp ||
                    converted.Duration != decoded.Duration || converted.Format.SampleAspectRatio != decoded.Format.SampleAspectRatio)
                {
                    throw new InvalidDataException("The sRGB output format or timing differs from the reference.");
                }
                for (int index = 0; index < expected.Length; index++)
                {
                    if (Math.Abs(expected[index] - converted.Data.Span[index]) > 1)
                    {
                        throw new InvalidDataException($"The sRGB output differs from the reference at byte {index}.");
                    }
                }
                VerifyPngPreview(converted, allocator);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The sRGB conversion retained output storage.");
            }
            Console.WriteLine("PASS public SDR conversion: sRGB reference within one 8-bit code per channel, metadata and allocation release match.");
        }

        private static void VerifyPngPreview(VideoFrame converted, MediaBufferAllocator allocator)
        {
            using VideoFrame preview = DisplayResize.Fit(converted, 100, 100, allocator);
            using MediaBuffer png = PngEncoder.Encode(preview, allocator);
            Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(png.Memory.Span);
            byte[] header = [0, 0, 0, 100, 0, 0, 0, 49, 8, 6, 0, 0, 0];
            byte[] physical = [0, 0, 0, 1, 0, 0, 0, 1, 0];
            if (!chunks["IHDR"].AsSpan().SequenceEqual(header) || !chunks["pHYs"].AsSpan().SequenceEqual(physical) ||
                !chunks["sRGB"].AsSpan().SequenceEqual(new byte[] { 0 }) || chunks["IEND"].Length != 0)
            {
                throw new InvalidDataException("The PNG preview dimensions, color or aspect differ from the reference.");
            }
            byte[] filtered = PngTestInput.Inflate(chunks["IDAT"]);
            if (filtered.Length != 401 * 49)
            {
                throw new InvalidDataException("Unexpected PNG scanline length.");
            }
            for (int y = 0; y < 49; y++)
            {
                if (filtered[y * 401] != 0)
                {
                    throw new InvalidDataException("Unexpected PNG scanline filter.");
                }
                for (int x = 0; x < 100; x++)
                {
                    ReadOnlySpan<byte> source = converted.GetRow(0, y * 62 / 49).Slice(x * 94 / 100 * 4, 4);
                    if (!source.SequenceEqual(filtered.AsSpan(y * 401 + 1 + x * 4, 4)))
                    {
                        throw new InvalidDataException("The PNG preview pixel does not match the selected source sample.");
                    }
                }
            }
            Console.WriteLine("PASS public PNG preview: square-pixel 100x49 sRGB, all CRCs and decoded pixels match.");
        }
    }
}
