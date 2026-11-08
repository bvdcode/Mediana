// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Streaming.Hls;
using Mediana.Execution;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal class HlsVideoRendition(Yuv420AreaResizer resizer, H264Encoder encoder, AvcHlsWriter writer,
        HlsRenditionOutput files) : IAsyncDisposable
    {
        private readonly MediaEncodePipeline<VideoFrame> pipeline = new(encoder, writer);
        public AvcHlsWriter Writer { get; } = writer;
        public VideoFormat Format => resizer.OutputFormat;

        public static HlsVideoRendition Create(VideoFormat source, EncodedStreamInfo video, EncodedStreamInfo? audio, MediaBufferAllocator allocator,
            int width, H264EncoderOptions encoderOptions, MediaDuration targetDuration, IHlsPackageOutput output, HlsBandwidthOptions bandwidth)
        {
            Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(source, width, source.Height, allocator);
            H264Encoder? encoder = null;
            bool success = false;
            try
            {
                long intervalTicks = new MediaTimestamp(targetDuration.Value, targetDuration.TimeBase)
                    .Rescale(TimeBase.TimeSpanTicks, TimestampRounding.Ceiling).Value;
                encoder = H264Encoder.Create(resizer.OutputFormat, video.TimeBase, allocator,
                    encoderOptions with { KeyFrameInterval = TimeSpan.FromTicks(intervalTicks) }, video.StreamIndex);
                string name = "variant-" + width.ToString(CultureInfo.InvariantCulture);
                HlsRenditionOutput files = new(output, name);
                AvcHlsWriteOptions options = new() { TargetDuration = targetDuration, Bandwidth = bandwidth };
                AvcHlsWriter writer = audio is null ? AvcHlsWriter.Create(encoder.Stream, files.OpenSegmentAsync, allocator, options) :
                    AvcHlsWriter.Create(encoder.Stream, audio, files.OpenSegmentAsync, allocator, options);
                HlsVideoRendition result = new(resizer, encoder, writer, files);
                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    encoder?.Dispose();
                    resizer.Dispose();
                }
            }
        }

        public async ValueTask WriteAsync(VideoFrame input, CancellationToken token)
        {
            using VideoFrame prepared = resizer.ResizeOrRetain(input, token);
            await pipeline.WriteAsync(prepared, token).ConfigureAwait(false);
        }

        public async ValueTask CompleteAsync(CancellationToken token)
        {
            await pipeline.CompleteInputAsync(token).ConfigureAwait(false);
            await Writer.CompleteAsync(token).ConfigureAwait(false);
        }

        public async ValueTask<HlsVariant> WritePlaylistAsync(long? targetDurationSeconds, CancellationToken token)
        {
            string playlist = Writer.CreateVodPlaylist(HlsRenditionOutput.SegmentName, targetDurationSeconds);
            await files.WritePlaylistAsync(playlist, token).ConfigureAwait(false);
            int? displayWidth = null;
            int? displayHeight = null;
            if (Format.SampleAspectRatio is SampleAspectRatio aspect)
            {
                long numerator = (long)Format.Width * aspect.Numerator;
                if (numerator % aspect.Denominator == 0 && numerator / aspect.Denominator <= int.MaxValue)
                {
                    displayWidth = (int)(numerator / aspect.Denominator);
                    displayHeight = Format.Height;
                }
            }
            return Writer.CreateVariant(files.PlaylistUri, displayWidth, displayHeight, targetDurationSeconds);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Writer.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                encoder.Dispose();
                resizer.Dispose();
            }
        }
    }
}
