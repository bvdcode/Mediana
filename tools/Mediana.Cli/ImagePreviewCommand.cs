// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal static class ImagePreviewCommand
    {
        private const long BufferBudgetBytes = 256L * 1024 * 1024;

        public static async Task<int> RunAsync(IImageDecoder decoder, string inputPath, string outputPath,
            ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            return await RunAsync(engine, source, outputPath, request, cancellationToken);
        }

        public static async Task<int> RunAsync(MediaEngine engine, IByteSource source, string outputPath,
            ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator, request, cancellationToken: cancellationToken))
            using (VideoFrame srgb = SrgbConverter.Apply(frame, allocator, cancellationToken: cancellationToken))
            using (MediaBuffer png = PngEncoder.Encode(srgb, allocator, cancellationToken: cancellationToken))
            {
                await CommandOutputFile.WriteAsync(outputPath, async output => await output.WriteAsync(png.Memory, cancellationToken), cancellationToken: cancellationToken);
                await Console.Out.WriteLineAsync($"PNG image: {frame.Format.Width}x{frame.Format.Height}; {png.Memory.Length} bytes; sRGB.");
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Image output buffers were not released.");
            }
            return 0;
        }

        public static Task<int> SvgAsync(string inputPath, string outputPath, int? width, CancellationToken cancellationToken = default) =>
            RunAsync(new SvgImageDecoder(renderOptions: new SvgRenderOptions { Width = width }), inputPath, outputPath, cancellationToken: cancellationToken);

        public static Task<int> TgsAsync(string inputPath, string outputPath, double seconds, int? width, CancellationToken cancellationToken = default) =>
            RunAsync(new TgsImageDecoder(renderOptions: new SvgRenderOptions { Width = width }), inputPath, outputPath,
                new ImageDecodeRequest { Time = new MediaTimestamp(TimeSpan.FromSeconds(seconds).Ticks, TimeBase.TimeSpanTicks) }, cancellationToken);
    }
}
