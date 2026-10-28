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
                if (reference.Length != 94 * 62 * 4)
                {
                    throw new InvalidDataException("The rotated sRGB reference has an unexpected length.");
                }
                PngTestInput.VerifyPreview(png.Memory.Span, 49, 100, reference, 94,
                    static (x, y) => ((99 - y) * 94 / 100, x * 62 / 49), 1);
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
