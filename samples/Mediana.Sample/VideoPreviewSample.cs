using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Sample
{
    internal static class VideoPreviewSample
    {
        private const long ProcessingBufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int streamIndex, long milliseconds,
            int maximumWidth, int maximumHeight)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using VideoSession session = await engine.OpenVideoAsync(source, streamIndex);
            MediaTimestamp target = new(milliseconds, new TimeBase(1, 1000));
            using VideoFrame decoded = await session.ReadFrameAtOrAfterAsync(target)
                ?? throw new InvalidDataException("There is no frame at or after the requested timestamp.");
            MediaBufferAllocator allocator = new(ProcessingBufferBudgetBytes);
            int width;
            int height;
            int encodedLength;
            using (VideoFrame srgb = SrgbConverter.Apply(decoded, allocator))
            using (VideoFrame preview = DisplayResize.Fit(srgb, maximumWidth, maximumHeight, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                width = preview.Format.Width;
                height = preview.Format.Height;
                encodedLength = png.Memory.Length;
                await SaveAsync(outputPath, png.Memory);
            }
            decoded.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Preview buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"PNG preview: {width}x{height}; {encodedLength} bytes; PTS {decoded.Timestamp?.Value} at {decoded.Timestamp?.TimeBase.Numerator}/{decoded.Timestamp?.TimeBase.Denominator}; square pixels, sRGB. Live buffer bytes: 0.");
            return 0;
        }

        private static async Task SaveAsync(string outputPath, ReadOnlyMemory<byte> data)
        {
            string destination = Path.GetFullPath(outputPath);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(data);
                }
                File.Move(temporary, destination);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
