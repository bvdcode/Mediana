// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal static class VideoPreviewCommand
    {
        private const long ProcessingBufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int streamIndex, long milliseconds,
            int maximumWidth, int maximumHeight, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using VideoSession session = await engine.OpenVideoAsync(source, streamIndex, cancellationToken: cancellationToken);
            MediaTimestamp target = new(milliseconds, new TimeBase(1, 1000));
            using VideoFrame decoded = await session.ReadFrameAtOrAfterAsync(target, cancellationToken)
                ?? throw new InvalidDataException("There is no frame at or after the requested timestamp.");
            VideoDisplayMatrix displayMatrix = session.Stream.DisplayMatrix
                ?? throw new NotSupportedException("The video stream has no display transform interpretation.");
            MediaBufferAllocator allocator = new(ProcessingBufferBudgetBytes);
            int width;
            int height;
            int encodedLength;
            using (VideoFrame preview = SrgbPreview.Fit(decoded, displayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken: cancellationToken))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator, cancellationToken: cancellationToken))
            {
                width = preview.Format.Width;
                height = preview.Format.Height;
                encodedLength = png.Memory.Length;
                await CommandOutputFile.WriteAsync(outputPath, async output => await output.WriteAsync(png.Memory, cancellationToken), cancellationToken: cancellationToken);
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
    }
}
