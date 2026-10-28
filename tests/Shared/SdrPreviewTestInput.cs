using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class SdrPreviewTestInput
    {
        public static async Task VerifyAsync(string root, string name, int frameIndex, int fps, int width, int height, int previewHeight)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame decoded = await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(450, new TimeBase(1, 1000)))
                ?? throw new InvalidDataException("The requested SDR preview frame is missing.");
            MediaTimestamp timestamp = decoded.Timestamp ?? throw new InvalidDataException("The SDR preview has no PTS.");
            MediaDuration duration = decoded.Duration ?? throw new InvalidDataException("The SDR preview has no duration.");
            if (timestamp.CompareTo(new MediaTimestamp(frameIndex, new TimeBase(1, fps))) != 0 ||
                new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(1, new TimeBase(1, fps))) != 0 ||
                decoded.Format.Width != width || decoded.Format.Height != height || decoded.Format.SampleAspectRatio != new SampleAspectRatio(4, 3) ||
                session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new InvalidDataException("The SDR preview selection, timing or display metadata differs from the reference.");
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, $"{name}.frame{frameIndex}.srgb.rgba"));
            if (reference.Length != width * height * 4)
            {
                throw new InvalidDataException("The sRGB reference has an unexpected length.");
            }
            MediaBufferAllocator allocator = new(128 * 1024);
            using (VideoFrame preview = SrgbPreview.Fit(decoded, session.Stream.DisplayMatrix.Value, 100, 100, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full);
                if (preview.Format.Width != 100 || preview.Format.Height != previewHeight || preview.Format.SampleAspectRatio != SampleAspectRatio.Square ||
                    preview.Format.Color != color || preview.Timestamp != decoded.Timestamp || preview.Duration != decoded.Duration)
                {
                    throw new InvalidDataException("The SDR preview has unexpected dimensions, color, aspect or timing.");
                }
                PngTestInput.VerifyPreview(png.Memory.Span, 100, previewHeight, reference, width,
                    (x, y) => (x * width / 100, y * height / previewHeight), 1);
            }
            decoded.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The SDR preview retained buffer storage.");
            }
        }
    }
}
