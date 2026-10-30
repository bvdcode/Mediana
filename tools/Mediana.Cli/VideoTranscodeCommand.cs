// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Execution;
using Mediana.Core.Timing;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
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

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, int quantizer,
            int? maximumWidth, int? maximumHeight, ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory())
                .AddVideoEncoder(new H264EncoderFactory(new() { Quantizer = quantizer }))
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = streamIndex is int index
                ? await engine.OpenVideoAsync(source, index, cancellationToken: cancellationToken)
                : await engine.OpenVideoAsync(source, cancellationToken: cancellationToken);
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
                    if (update.ProcessedDuration is MediaDuration duration)
                    {
                        progress.Report(duration, destination.Position, update.IsComplete);
                    }
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
            await Console.Out.WriteLineAsync($"MPEG-TS video encoding: {frames} frames; Baseline CAVLC intra; QP {quantizer}; video only. Live buffer bytes: 0.");
            return 0;
        }

    }
}
