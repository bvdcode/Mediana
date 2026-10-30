// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Streaming.Hls;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class HlsEncodeCommand
    {
        private const long BufferBudgetBytes = 128 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int videoIndex, int[] widths,
            int qp, long targetMilliseconds, ILogger logger, CancellationToken token)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source, videoIndex, cancellationToken: token);
            if (session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new NotSupportedException("Video encoding currently requires an identity display transform.");
            }
            using VideoFrame first = await session.ReadFrameAsync(token)
                ?? throw new InvalidDataException("The input contains no decoded video frames.");
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            long frames = 0;
            int segments = 0;
            await CommandOutputDirectory.WriteAsync(output, async directory =>
            {
                List<HlsVideoRendition> renditions = [];
                try
                {
                    HashSet<(int Width, int Height)> dimensions = [];
                    foreach (int width in widths)
                    {
                        HlsVideoRendition rendition = HlsVideoRendition.Create(first.Format, session.Stream.TimeBase,
                            allocator, width, qp, targetMilliseconds, directory);
                        renditions.Add(rendition);
                        if (!dimensions.Add((rendition.Format.Width, rendition.Format.Height)))
                        {
                            throw new ArgumentException("The selected width bounds produce duplicate encoded dimensions.", nameof(widths));
                        }
                    }
                    foreach (HlsVideoRendition rendition in renditions)
                    {
                        await rendition.WriteAsync(first, token);
                    }
                    first.Dispose();
                    frames++;
                    long reportedAt = TimeProvider.System.GetTimestamp();
                    while (await session.ReadFrameAsync(token) is VideoFrame frame)
                    {
                        using (frame)
                        {
                            foreach (HlsVideoRendition rendition in renditions)
                            {
                                await rendition.WriteAsync(frame, token);
                            }
                            frames++;
                        }
                        if (TimeProvider.System.GetElapsedTime(reportedAt) >= TimeSpan.FromSeconds(1))
                        {
                            logger.LogInformation("HLS encoding: {Frames} input frames; {Variants} variants; {Bytes} output bytes.",
                                frames, renditions.Count, renditions.Sum(rendition => rendition.Writer.BytesWritten));
                            reportedAt = TimeProvider.System.GetTimestamp();
                        }
                    }
                    List<HlsVariant> variants = [];
                    foreach (HlsVideoRendition rendition in renditions)
                    {
                        variants.Add(await rendition.CompleteAsync(token));
                    }
                    ValidateAlignment(renditions);
                    await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"), HlsMasterPlaylist.Create(variants), new UTF8Encoding(false), token);
                    segments = renditions[0].Writer.Segments.Count;
                }
                finally
                {
                    await Task.WhenAll(renditions.Select(rendition => rendition.DisposeAsync().AsTask()));
                }
                await session.DisposeAsync();
                if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidOperationException("HLS encoding buffers were not released.");
                }
            }, token);
            await Console.Out.WriteLineAsync($"HLS video encoding: {frames} input frames; {widths.Length} variants; {segments} aligned segments per variant. Live buffer bytes: 0.");
            return 0;
        }

        private static void ValidateAlignment(IReadOnlyList<HlsVideoRendition> renditions)
        {
            IReadOnlyList<HlsMediaSegment> first = renditions[0].Writer.Segments;
            foreach (HlsVideoRendition rendition in renditions.Skip(1))
            {
                IReadOnlyList<HlsMediaSegment> current = rendition.Writer.Segments;
                if (first.Count != current.Count || first.Where((segment, index) =>
                    segment.Start != current[index].Start || segment.Duration != current[index].Duration).Any())
                {
                    throw new InvalidOperationException("HLS variant segment timelines differ.");
                }
            }
        }
    }
}
