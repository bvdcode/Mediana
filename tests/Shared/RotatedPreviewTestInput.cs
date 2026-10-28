using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class RotatedPreviewTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-weighted-rotated.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            VideoDisplayMatrix matrix = new(0, -1, 0, 1, 0, 0, 0, 0, 1);
            if (session.Stream.DisplayMatrix != matrix)
            {
                throw new InvalidDataException("The rotated MP4 does not preserve the independently inspected display matrix.");
            }
            using VideoFrame decoded = await session.ReadFrameAsync()
                ?? throw new InvalidDataException("The rotated preview source frame is missing.");
            if (decoded.Format.Width != 94 || decoded.Format.Height != 62 || decoded.Format.SampleAspectRatio != new SampleAspectRatio(4, 3))
            {
                throw new InvalidDataException("The decoder changed storage geometry before explicit display fitting.");
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.frame0.srgb.rgba"));
            MediaBufferAllocator allocator = new(128 * 1024);
            using (VideoFrame preview = SrgbPreview.Fit(decoded, matrix, 100, 100, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full);
                if (preview.Format.Width != 49 || preview.Format.Height != 100 || preview.Format.SampleAspectRatio != SampleAspectRatio.Square ||
                    preview.Format.Color != color || preview.Timestamp != decoded.Timestamp || preview.Duration != decoded.Duration)
                {
                    throw new InvalidDataException("The rotated preview has unexpected dimensions, color, aspect or timing.");
                }
                Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(png.Memory.Span);
                byte[] header = [0, 0, 0, 49, 0, 0, 0, 100, 8, 6, 0, 0, 0];
                byte[] physical = [0, 0, 0, 1, 0, 0, 0, 1, 0];
                if (!chunks["IHDR"].AsSpan().SequenceEqual(header) || !chunks["pHYs"].AsSpan().SequenceEqual(physical) ||
                    !chunks["sRGB"].AsSpan().SequenceEqual(new byte[] { 0 }) || chunks["IEND"].Length != 0)
                {
                    throw new InvalidDataException("The rotated PNG does not carry the expected image metadata.");
                }
                byte[] pixels = PngTestInput.Inflate(chunks["IDAT"]);
                if (pixels.Length != 197 * 100 || reference.Length != 94 * 62 * 4)
                {
                    throw new InvalidDataException("Unexpected rotated PNG or reference data length.");
                }
                for (int y = 0; y < 100; y++)
                {
                    if (pixels[y * 197] != 0)
                    {
                        throw new InvalidDataException("Unexpected PNG scanline filter.");
                    }
                    for (int x = 0; x < 49; x++)
                    {
                        int sourceOffset = ((x * 62 / 49) * 94 + (99 - y) * 94 / 100) * 4;
                        int outputOffset = y * 197 + 1 + x * 4;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            if (Math.Abs(pixels[outputOffset + channel] - reference[sourceOffset + channel]) > 1)
                            {
                                throw new InvalidDataException("A rotated PNG pixel differs from the independent reference.");
                            }
                        }
                    }
                }
            }
            decoded.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Rotated preview storage was not released.");
            }
        }
    }
}
