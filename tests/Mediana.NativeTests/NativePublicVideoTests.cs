using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativePublicVideoTests
    {
        public static async Task RunAsync(string root)
        {
            List<byte> baseline = [];
            for (int index = 0; index < 6; index++)
            {
                baseline.AddRange(await File.ReadAllBytesAsync(Path.Combine(root, $"avc-tail.frame{index}.yuv")));
            }
            await VerifyAsync(Path.Combine(root, "avc-tail.mov"), baseline.ToArray(), 6, 80, 48, 5, SampleAspectRatio.Square);
            await VerifyAsync(Path.Combine(root, "avc-cropped.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-cropped.frame0.yuv")), 1, 78, 46, 1, SampleAspectRatio.Square);
            await VerifyAsync(Path.Combine(root, "avc-weighted.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-weighted.sequence.yuv")), 12, 94, 62, 10, new SampleAspectRatio(4, 3));
            await VerifyAsync(Path.Combine(root, "avc-cabac-intra.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-cabac-intra.sequence.yuv")), 6, 94, 62, 5, new SampleAspectRatio(4, 3));
            for (int initialization = 0; initialization < 3; initialization++)
            {
                string name = $"avc-cabac-p{initialization}";
                await VerifyAsync(Path.Combine(root, name + ".mp4"),
                    await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv")), 12, 94, 62, 10, new SampleAspectRatio(4, 3));
            }
            await VerifyAsync(Path.Combine(root, "avc-cabac-motion.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-cabac-motion.sequence.yuv")), 18, 190, 126, 12, SampleAspectRatio.Square);
            await VerifyAsync(Path.Combine(root, "avc-cavlc-8x8.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-cavlc-8x8.sequence.yuv")), 18, 190, 126, 12, SampleAspectRatio.Square);
            await VerifyAsync(Path.Combine(root, "avc-cavlc-8x8-weighted.mp4"),
                await File.ReadAllBytesAsync(Path.Combine(root, "avc-cavlc-8x8-weighted.sequence.yuv")), 12, 190, 126, 12, new SampleAspectRatio(4, 3));
            for (int initialization = 0; initialization < 3; initialization++)
            {
                string name = $"avc-cabac-8x8-{initialization}";
                await VerifyAsync(Path.Combine(root, name + ".mp4"), await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv")),
                    initialization == 0 ? 18 : 12, 190, 126, 12, initialization == 0 ? SampleAspectRatio.Square : new SampleAspectRatio(4, 3));
            }
        }

        private static async Task VerifyAsync(string path, ReadOnlyMemory<byte> expected, int count, int width,
            int height, int fps, SampleAspectRatio aspect)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            int frameBytes = width * height * 3 / 2;
            if (expected.Length != count * frameBytes)
            {
                throw new InvalidDataException("The public video reference sequence has an unexpected length.");
            }
            for (int index = 0; index < count; index++)
            {
                using VideoFrame frame = await session.ReadFrameAsync()
                    ?? throw new InvalidDataException("The public video sequence ended early.");
                if (frame.Format.Width != width || frame.Format.Height != height || frame.Format.PixelFormat != VideoPixelFormat.Yuv420P8 ||
                    frame.Format.SampleAspectRatio != aspect || frame.Format.Color.Range != ColorRange.Limited ||
                    frame.Format.Color.ChromaLocation != ChromaLocation.Left)
                {
                    throw new InvalidDataException("The public video display metadata differs from the reference.");
                }
                MediaTimestamp timestamp = frame.Timestamp ?? throw new InvalidDataException("The public video timestamp is missing.");
                MediaDuration duration = frame.Duration ?? throw new InvalidDataException("The public video duration is missing.");
                if (timestamp.CompareTo(new MediaTimestamp(index, new TimeBase(1, fps))) != 0 ||
                    new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(1, new TimeBase(1, fps))) != 0)
                {
                    throw new InvalidDataException("The public video timing differs from the reference.");
                }
                H264TestInput.VerifyFrame(frame, expected.Span.Slice(index * frameBytes, frameBytes));
            }
            if (await session.ReadFrameAsync() is not null)
            {
                throw new InvalidDataException("The public video sequence contains unexpected output.");
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The public video session retained buffer storage.");
            }
            Console.WriteLine($"PASS public video {Path.GetFileName(path)}: {count} pictures, timing, crop, SAR and all YUV samples match.");
        }
    }
}
