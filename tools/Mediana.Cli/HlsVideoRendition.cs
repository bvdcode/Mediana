// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.Cli
{
    internal class HlsVideoRendition(Yuv420AreaResizer resizer, H264Encoder encoder, AvcHlsWriter writer,
        string directory, string name) : IAsyncDisposable
    {
        public AvcHlsWriter Writer { get; } = writer;
        public VideoFormat Format => resizer.OutputFormat;

        public static HlsVideoRendition Create(VideoFormat source, EncodedStreamInfo video, EncodedStreamInfo? audio, MediaBufferAllocator allocator,
            int width, int qp, long targetMilliseconds, string root)
        {
            Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(source, width, source.Height, allocator);
            H264Encoder? encoder = null;
            bool success = false;
            try
            {
                encoder = H264Encoder.Create(resizer.OutputFormat, video.TimeBase, allocator, new() { Quantizer = qp }, video.StreamIndex);
                string name = "variant-" + width.ToString(CultureInfo.InvariantCulture);
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                ValueTask<Stream> OpenSegment(int number, CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    Stream destination = new FileStream(Path.Combine(directory, SegmentName(number)), FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous);
                    return ValueTask.FromResult(destination);
                }
                AvcHlsWriteOptions options = new() { TargetDuration = new(targetMilliseconds, new(1, 1000)) };
                AvcHlsWriter writer = audio is null ? AvcHlsWriter.Create(encoder.Stream, OpenSegment, allocator, options) :
                    AvcHlsWriter.Create(encoder.Stream, audio, OpenSegment, allocator, options);
                HlsVideoRendition result = new(resizer, encoder, writer, directory, name);
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

        public ValueTask WriteAsync(VideoFrame input, CancellationToken token) =>
            VideoEncoding.WriteAsync(encoder, Writer, input, resizer, token);

        public async ValueTask<HlsVariant> CompleteAsync(CancellationToken token)
        {
            await VideoEncoding.CompleteAsync(encoder, Writer, token);
            string playlist = Writer.CreateVodPlaylist(SegmentName);
            await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"), playlist, new UTF8Encoding(false), token);
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
            return Writer.CreateVariant(name + "/index.m3u8", displayWidth, displayHeight);
        }

        private static string SegmentName(int number) => "segment-" + number.ToString(CultureInfo.InvariantCulture) + ".ts";

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Writer.DisposeAsync();
            }
            finally
            {
                encoder.Dispose();
                resizer.Dispose();
            }
        }
    }
}
