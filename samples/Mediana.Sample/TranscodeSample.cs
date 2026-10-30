// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class TranscodeSample
    {
        internal static async Task<int> RunAsync(string input, string output)
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
            using VideoFrame first = await session.ReadFrameAsync()
                ?? throw new InvalidDataException("The input contains no video frames.");
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
                return session.ReadFrameAsync(token);
            }
        }
    }
}
