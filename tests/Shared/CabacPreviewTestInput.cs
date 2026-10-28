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
    internal static class CabacPreviewTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-cabac-intra.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame decoded = await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(450, new TimeBase(1, 1000)))
                ?? throw new InvalidDataException("The requested CABAC preview frame is missing.");
            MediaTimestamp timestamp = decoded.Timestamp ?? throw new InvalidDataException("The CABAC preview has no PTS.");
            MediaDuration duration = decoded.Duration ?? throw new InvalidDataException("The CABAC preview has no duration.");
            if (timestamp.CompareTo(new MediaTimestamp(600, new TimeBase(1, 1000))) != 0 ||
                new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(200, new TimeBase(1, 1000))) != 0 ||
                decoded.Format.Width != 94 || decoded.Format.Height != 62 || decoded.Format.SampleAspectRatio != new SampleAspectRatio(4, 3) ||
                session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new InvalidDataException("The CABAC preview selection, timing or display metadata differs from the reference.");
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "avc-cabac-intra.frame3.srgb.rgba"));
            if (reference.Length != 94 * 62 * 4)
            {
                throw new InvalidDataException("The CABAC sRGB reference has an unexpected length.");
            }
            MediaBufferAllocator allocator = new(128 * 1024);
            using (VideoFrame preview = SrgbPreview.Fit(decoded, session.Stream.DisplayMatrix.Value, 100, 100, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full);
                if (preview.Format.Width != 100 || preview.Format.Height != 49 || preview.Format.SampleAspectRatio != SampleAspectRatio.Square ||
                    preview.Format.Color != color || preview.Timestamp != decoded.Timestamp || preview.Duration != decoded.Duration)
                {
                    throw new InvalidDataException("The CABAC preview has unexpected dimensions, color, aspect or timing.");
                }
                PngTestInput.VerifyPreview(png.Memory.Span, 100, 49, reference, 94,
                    static (x, y) => (x * 94 / 100, y * 62 / 49), 1);
            }
            decoded.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The CABAC preview retained buffer storage.");
            }
        }
    }
}
