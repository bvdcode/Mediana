// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Sample
{
    internal static class HlsSampleVideoEncoding
    {
        public static async Task<TResult> RunAsync<TWriter, TResult>(string input, MediaTimestamp start,
            MediaTimestamp end, PreparedHlsSampleOptions options,
            Func<EncodedStreamInfo, MediaBufferAllocator, TWriter> createWriter,
            Func<TWriter, EncodedStreamInfo, VideoFormat, TResult> summarize, CancellationToken token)
            where TWriter : IContainerWriter
        {
            MediaBufferAllocator allocator = new(options.BufferBudgetBytes);
            TResult result;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(input)))
            await using (VideoSession session = await new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build()
                .OpenVideoAsync(source, new() { BufferBudgetBytes = options.BufferBudgetBytes }, token))
            {
                if (session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
                {
                    throw new NotSupportedException("This sample requires an identity video display transform.");
                }
                await session.SeekAsync(start, token);
                await using VideoTrimmer selection = new(session.ReadFrameAsync, start, end);
                using VideoFrame first = await selection.ReadFrameAsync(token)
                    ?? throw new InvalidDataException("The selected interval contains no video pictures.");
                if (first.Timestamp != start)
                {
                    throw new NotSupportedException("This sample requires presentation boundaries at video picture timestamps.");
                }
                using Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(first.Format,
                    options.MaximumWidth, first.Format.Height, allocator, token);
                using H264Encoder encoder = H264Encoder.Create(resizer.OutputFormat, session.Stream.TimeBase,
                    allocator, new() { KeyFrameInterval = options.KeyFrameInterval });
                await using TWriter writer = createWriter(encoder.Stream, allocator);
                bool opening = true;
                MediaTimestamp? actualEnd = null;
                await MediaOperations.EncodeVideoAsync(ReadAsync, encoder, writer, cancellationToken: token);
                if (actualEnd != end)
                {
                    throw new NotSupportedException("This sample requires the selected end to coincide with the end of a video picture.");
                }
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidOperationException("The video session retained media buffers after disposal.");
                }
                result = summarize(writer, encoder.Stream, resizer.OutputFormat);

                async ValueTask<VideoFrame?> ReadAsync(CancellationToken cancellationToken)
                {
                    using VideoFrame? frame = opening ? first.Retain() : await selection.ReadFrameAsync(cancellationToken);
                    if (opening)
                    {
                        opening = false;
                        first.Dispose();
                    }
                    if (frame is null)
                    {
                        return null;
                    }
                    MediaTimestamp timestamp = frame.Timestamp
                        ?? throw new InvalidDataException("The video picture has no presentation timestamp.");
                    MediaDuration duration = frame.Duration
                        ?? throw new InvalidDataException("The video picture has no duration.");
                    MediaTimestamp length = new(duration.Value, duration.TimeBase);
                    MediaTimestamp ticks = length.Rescale(timestamp.TimeBase, TimestampRounding.TowardZero);
                    if (ticks != length)
                    {
                        throw new NotSupportedException("The video picture duration is not exact on its presentation clock.");
                    }
                    actualEnd = new(checked(timestamp.Value + ticks.Value), timestamp.TimeBase);
                    return resizer.ResizeOrRetain(frame, cancellationToken);
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Video encoding retained media buffers after disposal.");
            }
            return result;
        }
    }
}
