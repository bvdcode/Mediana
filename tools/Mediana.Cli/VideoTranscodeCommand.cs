// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Execution;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class VideoTranscodeCommand
    {
        private const long BufferBudgetBytes = 128 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, H264EncoderOptions options,
            int? maximumWidth, int? maximumHeight, bool videoOnly, ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie)
                .AddVideoDecoder(new H264DecoderFactory())
                .AddVideoEncoder(new H264EncoderFactory(options))
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using ContainerSession container = await engine.OpenContainerAsync(source, cancellationToken: cancellationToken);
            if (!videoOnly && container.Streams.Any(stream => stream.Kind == MediaKind.Audio))
            {
                throw new NotSupportedException("The input contains audio. Combined video encoding and audio output is not supported by this command. Use --video-only to explicitly omit audio.");
            }
            await using VideoSession session = engine.OpenVideo(container, streamIndex, cancellationToken);
            if (session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new NotSupportedException("Video encoding currently requires an identity display transform.");
            }
            using VideoFrame first = await session.ReadFrameAsync(cancellationToken)
                ?? throw new InvalidDataException("The input contains no decoded video frames.");
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using Yuv420AreaResizer? resizer = maximumWidth is not null || maximumHeight is not null
                ? Yuv420AreaResizer.CreateFit(first.Format, maximumWidth ?? first.Format.Width,
                    maximumHeight ?? first.Format.Height, allocator, cancellationToken) : null;
            using IMediaEncoder<VideoFrame> encoder = engine.CreateVideoEncoder("h264", resizer?.OutputFormat ?? first.Format, session.Stream.TimeBase, allocator);
            long frames = 0;
            MediaProgress progress = new(logger, TimeProvider.System, session.Stream.CodedDuration);
            await outputFile.WriteAsync(async destination =>
            {
                await using IContainerWriter writer = engine.CreateContainerWriter("mpegts", destination, [encoder.Stream], allocator);
                bool opening = true;
                MediaOperationProgress result = await MediaOperations.EncodeVideoAsync(ReadAsync, encoder, writer, update =>
                {
                    progress.Report(update.ProcessedDuration, destination.Position, update.IsComplete);
                }, cancellationToken);
                frames = result.Frames;

                async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
                {
                    using VideoFrame? frame = opening ? first.Retain() : await session.ReadFrameAsync(token);
                    if (opening)
                    {
                        opening = false;
                        first.Dispose();
                    }
                    return frame is null ? null : resizer is null ? frame.Retain() : resizer.Apply(frame, token);
                }
            }, cancellationToken);
            encoder.Dispose();
            resizer?.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Video encoding buffers were not released.");
            }
            string quality = options.RateControl is H264RateControlOptions rate
                ? $"variable packet rate {rate.BitRate} bits/s; initial QP {options.Quantizer}" : $"QP {options.Quantizer}";
            await Console.Out.WriteLineAsync($"MPEG-TS video encoding: {frames} frames; Baseline CAVLC I/P; {quality}; video only. Live buffer bytes: 0.");
            return 0;
        }

    }
}
