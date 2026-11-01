// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
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
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaOpenOptions options = new() { BufferBudgetBytes = BufferBudgetBytes };
            MediaInfo info = await engine.ProbeAsync(source, options, cancellationToken);
            VideoDisplayMatrix displayMatrix = info.Streams.First(stream => stream.Kind == Core.MediaKind.Video).DisplayMatrix
                ?? throw new NotSupportedException("The preview requires a known display transform.");
            using VideoFrame frame = await SelectFrameAsync(engine, source, info, options, cancellationToken);
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using VideoFrame preview = SrgbPreview.Fit(frame, displayMatrix, MaximumSize, MaximumSize, allocator,
                cancellationToken);
            await using FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await PngEncoder.EncodeAsync(preview, destination, allocator, cancellationToken);
            await Console.Out.WriteLineAsync($"Preview: {preview.Format.Width}x{preview.Format.Height}, sRGB; selected PTS {frame.Timestamp?.Value} at {frame.Timestamp?.TimeBase.Numerator}/{frame.Timestamp?.TimeBase.Denominator}.");
            return 0;
        }

        internal static async ValueTask<VideoFrame> SelectFrameAsync(MediaEngine engine, IRandomAccessByteSource source,
            MediaInfo info, MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(info);
            MediaDuration declared = info.Container?.DeclaredDuration
                ?? throw new NotSupportedException("The preview sample requires a declared container duration.");
            MediaTimestamp duration = new(declared.Value, declared.TimeBase);
            MediaTimestamp target = duration.CompareTo(new(1, new(1, 1))) > 0
                ? new(duration.Rescale(TimeBase.TimeSpanTicks, TimestampRounding.Floor).Value / 2, TimeBase.TimeSpanTicks)
                : new(0, TimeBase.TimeSpanTicks);
            await using VideoSession session = await engine.OpenVideoAsync(source, options, cancellationToken);
            await session.SeekAsync(target, cancellationToken);
            return await session.ReadFrameAtOrAfterAsync(target, cancellationToken)
                ?? throw new InvalidDataException("There is no video frame at or after the preview target.");
        }
    }
}
