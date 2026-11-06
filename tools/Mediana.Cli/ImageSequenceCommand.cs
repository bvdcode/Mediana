// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class ImageSequenceCommand
    {
        internal static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? stream, long milliseconds,
            ILogger logger, TextWriter writer, IImageSequenceEncoder encoder, CancellationToken token)
        {
            MediaEngine images = ImageEngineFactory.Create();
            byte[] prefix = new byte[images.ImageProbeLength];
            await using FileStream file = File.OpenRead(input);
            int length = await file.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken: token);
            bool isImage = images.RecognizeImage(prefix.AsSpan(0, length), file.Length) is not null;
            file.Position = 0;
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            MediaTimestamp start = new(milliseconds, new(1, 1000));
            HashSet<string> warnings = [];
            void Warn(string warning) { if (warnings.Add(warning)) { logger.LogWarning("{Warning}", warning); } }
            if (isImage)
            {
                if (stream is not null) { throw new NotSupportedException("Animation input does not select a container stream."); }
                ImageDecodeRequest request = new() { Time = start, WarningHandler = Warn };
                await EncodeAsync(images.DecodeImageFramesAsync(source, allocator, request, CommandImageResources.OpenOptions, token),
                    outputFile, encoder, VideoDisplayMatrix.Identity, allocator, logger, writer, Warn, null, token);
            }
            else
            {
                MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).AddVideoDecoder(new H264DecoderFactory()).Build();
                await using VideoSession session = stream is int selected
                    ? await engine.OpenVideoAsync(source, selected, cancellationToken: token)
                    : await engine.OpenVideoAsync(source, cancellationToken: token);
                VideoDisplayMatrix matrix = session.Stream.DisplayMatrix ?? throw new NotSupportedException("The video stream has no display transform interpretation.");
                if (milliseconds != 0) { await session.SeekAsync(start, token); }
                await EncodeAsync(ReadVideoAsync(session, start, token), outputFile, encoder, matrix, allocator, logger, writer, Warn, session.Stream.CodedDuration, token);
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0) { throw new InvalidOperationException("Video animation decoding buffers were not released."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Animation output buffers were not released."); }
            return 0;
        }

        private static async Task EncodeAsync(IAsyncEnumerable<VideoFrame> frames, CommandOutputFile outputFile, IImageSequenceEncoder encoder,
            VideoDisplayMatrix matrix, MediaBufferAllocator allocator, ILogger logger, TextWriter writer, Action<string> warn, MediaDuration? total, CancellationToken token)
        {
            long count = 0;
            long ticks = 0;
            long bytes = 0;
            MediaProgress progress = new(logger, TimeProvider.System, total);
            VideoFrameTimeQuantizer clock = new(encoder.DurationTimeBase);
            await outputFile.WriteAsync(async output =>
            {
                bytes = await encoder.EncodeAsync(PrepareAsync(token), output, allocator, token);
                progress.Report(new(ticks, encoder.DurationTimeBase), bytes, completed: true);

                async IAsyncEnumerable<VideoFrame> PrepareAsync([EnumeratorCancellation] CancellationToken cancellation)
                {
                    await foreach (VideoFrame frame in frames.WithCancellation(cancellation))
                    {
                        using (frame)
                        using (VideoFrame pixels = SrgbPreview.FitWithColorPolicy(frame, ResolveColor, matrix,
                            int.MaxValue, int.MaxValue, allocator, cancellation))
                        using (VideoFrame timed = clock.Apply(pixels))
                        {
                            yield return timed;
                            ticks = checked(ticks + timed.Duration!.Value.Value);
                            count++;
                            progress.Report(new(ticks, encoder.DurationTimeBase), output.Position);
                        }
                    }
                }

                VideoColorInfo ResolveColor(VideoFormat format)
                {
                    VideoColorInfo color = SdrPreviewColorPolicy.Resolve(format);
                    if (color != format.GetColorInterpretation())
                    {
                        warn($"Missing source color labels were interpreted as {color.Primaries}, {color.Transfer}, {color.Matrix} for image output.");
                    }
                    return color;
                }
            }, token);
            await writer.WriteLineAsync($"{encoder.FormatId.ToUpperInvariant()} animation: {count} frames; {bytes} bytes; sRGB. Live buffer bytes: 0.");
        }

        private static async IAsyncEnumerable<VideoFrame> ReadVideoAsync(VideoSession session, MediaTimestamp start,
            [EnumeratorCancellation] CancellationToken token)
        {
            VideoFrame? first = await session.ReadFrameAtOrAfterAsync(start, token);
            if (first is null) { yield break; }
            yield return first;
            while (await session.ReadFrameAsync(token) is VideoFrame frame) { yield return frame; }
        }
    }
}
