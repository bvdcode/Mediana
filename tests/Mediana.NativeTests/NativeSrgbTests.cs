using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

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
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The sRGB conversion retained output storage.");
            }
            Console.WriteLine("PASS public SDR conversion: sRGB reference within one 8-bit code per channel, metadata and allocation release match.");
        }
    }
}
