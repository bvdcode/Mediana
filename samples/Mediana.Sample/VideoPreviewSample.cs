// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Sample
{
    internal static class VideoPreviewSample
    {
        private const int MaximumSize = 150;
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        internal static async Task<int> RunAsync(string input, string output, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddContainer(new MatroskaReaderFactory()).AddContainer(new Mp3ReaderFactory()).AddContainer(new FlacReaderFactory())
                .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using ContainerSession container = await engine.OpenContainerAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = BufferBudgetBytes }, cancellationToken);
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using VideoFrame preview = await ReadPreviewAsync(engine, container, allocator, cancellationToken);
            await using FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await PngEncoder.EncodeAsync(preview, destination, allocator, cancellationToken);
            await Console.Out.WriteLineAsync($"Preview: {preview.Format.Width}x{preview.Format.Height}, sRGB; selected PTS {preview.Timestamp?.Value} at {preview.Timestamp?.TimeBase.Numerator}/{preview.Timestamp?.TimeBase.Denominator}.");
            return 0;
        }

        internal static async ValueTask<VideoFrame> ReadPreviewAsync(MediaEngine engine, ContainerSession container,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            using ContainerImage? image = await engine.ReadContainerImageAsync(container,
                new([ContainerImageSource.Attachment, ContainerImageSource.Picture]), cancellationToken);
            if (image is not null)
            {
                await container.DisposeAsync();
                using VideoFrame frame = await engine.DecodeImageAsync(image.Data, allocator, cancellationToken: cancellationToken);
                return await FitAsync(frame, VideoDisplayMatrix.Identity, allocator, cancellationToken);
            }
            MediaTimestamp target = SelectTarget(container.Info.DeclaredDuration);
            await using VideoSession session = engine.OpenVideo(container, cancellationToken: cancellationToken);
            VideoDisplayMatrix matrix = session.Stream.DisplayMatrix
                ?? throw new NotSupportedException("The preview requires a known display transform.");
            using VideoFrame selected = await SelectFrameAsync(session, target, cancellationToken);
            return await FitAsync(selected, matrix, allocator, cancellationToken);
        }

        internal static async ValueTask<VideoFrame> SelectFrameAsync(MediaEngine engine, IRandomAccessByteSource source,
            MediaInfo info, MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(info);
            await using VideoSession session = await engine.OpenVideoAsync(source, options, cancellationToken);
            return await SelectFrameAsync(session, SelectTarget(info.Container?.DeclaredDuration), cancellationToken);
        }

        internal static MediaTimestamp SelectTarget(MediaDuration? declared)
        {
            MediaTimestamp zero = new(0, TimeBase.TimeSpanTicks);
            if (declared is not MediaDuration value) { return zero; }
            MediaTimestamp duration = new(value.Value, value.TimeBase);
            return duration.CompareTo(new(1, new(1, 1))) > 0
                ? new(duration.Rescale(TimeBase.TimeSpanTicks, TimestampRounding.Floor).Value / 2, TimeBase.TimeSpanTicks)
                : zero;
        }

        private static async ValueTask<VideoFrame> SelectFrameAsync(VideoSession session, MediaTimestamp target,
            CancellationToken cancellationToken)
        {
            if (session.CanSeek) { await session.SeekAsync(target, cancellationToken); }
            return await session.ReadFrameAtOrAfterAsync(target, cancellationToken)
                ?? throw new InvalidDataException("There is no video frame at or after the preview target.");
        }

        internal static async ValueTask<VideoFrame> FitAsync(VideoFrame frame, VideoDisplayMatrix matrix,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            VideoColorInfo color = SdrPreviewColorPolicy.Resolve(frame.Format);
            if (color != frame.Format.GetColorInterpretation())
            {
                await Console.Error.WriteLineAsync($"Preview interprets missing source color labels as {color.Primaries}/{color.Transfer}/{color.Matrix}.");
            }
            return SrgbPreview.Fit(frame, color, matrix, MaximumSize, MaximumSize, allocator, token);
        }
    }
}
