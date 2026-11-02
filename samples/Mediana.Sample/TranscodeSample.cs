// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Sample
{
    internal static class TranscodeSample
    {
        internal static Task<int> RunAsync(string input, string output) => RunCoreAsync(input, output, null, null);

        internal static Task<int> RunWindowAsync(string input, string output, long startMilliseconds, long endMilliseconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(startMilliseconds);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endMilliseconds, startMilliseconds);
            return RunCoreAsync(input, output, new(startMilliseconds, new(1, 1000)), new(endMilliseconds, new(1, 1000)));
        }

        private static async Task<int> RunCoreAsync(string input, string output, MediaTimestamp? start, MediaTimestamp? end)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory())
                .AddVideoEncoder(new H264EncoderFactory())
                .AddContainerWriter(new MpegTsWriterFactory())
                .Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            if (session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new NotSupportedException("This sample requires an identity display transform.");
            }
            await using VideoTrimmer? window = start is MediaTimestamp from ? new(session.ReadFrameAsync, from, end) : null;
            if (start is MediaTimestamp target)
            {
                await session.SeekAsync(target);
            }
            using VideoFrame first = await ReadNextAsync(default)
                ?? throw new InvalidDataException("The selected input contains no video frames.");
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using IMediaEncoder<VideoFrame> encoder = engine.CreateVideoEncoder("h264", first.Format, session.Stream.TimeBase, allocator);
            await using FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using IContainerWriter writer = engine.CreateContainerWriter("mpegts", destination, [encoder.Stream], allocator);
            bool opening = true;
            MediaOperationProgress result = await MediaOperations.EncodeVideoAsync(ReadAsync, encoder, writer);
            await Console.Out.WriteLineAsync($"Encoded {result.Frames} video frames into {result.Packets} packets.");
            return 0;

            ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                if (opening)
                {
                    opening = false;
                    return ValueTask.FromResult<VideoFrame?>(first.Retain());
                }
                return ReadNextAsync(token);
            }

            ValueTask<VideoFrame?> ReadNextAsync(CancellationToken token) =>
                window is null ? session.ReadFrameAsync(token) : window.ReadFrameAsync(token);
        }
    }
}
