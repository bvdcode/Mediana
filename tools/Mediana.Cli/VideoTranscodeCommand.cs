// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class VideoTranscodeCommand
    {
        private const long BufferBudgetBytes = 128 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int? streamIndex, int quantizer,
            ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
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
            using H264Encoder encoder = H264Encoder.Create(first.Format, session.Stream.TimeBase, allocator,
                new() { Quantizer = quantizer });
            long frames = 0;
            long reportedAt = TimeProvider.System.GetTimestamp();
            await CommandOutputFile.WriteAsync(output, async destination =>
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(destination, [encoder.Stream], allocator);
                await WriteAsync(encoder, writer, first, cancellationToken);
                frames++;
                while (true)
                {
                    using VideoFrame? frame = await session.ReadFrameAsync(cancellationToken);
                    if (frame is null)
                    {
                        break;
                    }
                    await WriteAsync(encoder, writer, frame, cancellationToken);
                    frames++;
                    if (TimeProvider.System.GetElapsedTime(reportedAt) >= TimeSpan.FromSeconds(1))
                    {
                        logger.LogInformation("Video encoding: {Frames} frames; {Bytes} output bytes.", frames, destination.Position);
                        reportedAt = TimeProvider.System.GetTimestamp();
                    }
                }
                encoder.CompleteInput();
                if (encoder.Receive(cancellationToken).Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidOperationException("The intra encoder retained delayed output.");
                }
                await writer.CompleteAsync(cancellationToken);
            }, cancellationToken);
            encoder.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Video encoding buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"MPEG-TS video encoding: {frames} frames; Baseline CAVLC intra; QP {quantizer}; video only. Live buffer bytes: 0.");
            return 0;
        }

        private static async ValueTask WriteAsync(H264Encoder encoder, MpegTsWriter writer, VideoFrame frame, CancellationToken token)
        {
            if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
            {
                throw new InvalidOperationException("The intra encoder did not accept its next frame.");
            }
            using MediaPacket packet = encoder.Receive(token).Value
                ?? throw new InvalidOperationException("The intra encoder produced no packet.");
            await writer.WritePacketAsync(packet, token);
        }
    }
}
