// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Cli
{
    internal static class HlsCopyCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int videoIndex, int? audioIndex, long targetMilliseconds, CancellationToken cancellationToken = default)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            int count = 0;
            await CommandOutputDirectory.WriteAsync(output, async directory =>
            {
                await using (RandomAccessStreamByteSource source = new(File.OpenRead(input)))
                await using (Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, videoIndex, audioIndex,
                    new() { Planning = new() { TargetDuration = new(targetMilliseconds, new TimeBase(1, 1000)) } }, cancellationToken: cancellationToken))
                {
                    foreach (Mp4HlsSegment segment in session.Segments)
                    {
                        string path = Path.Combine(directory, SegmentName(segment.Number));
                        await using FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            bufferSize: 4096, FileOptions.Asynchronous);
                        await session.WriteSegmentAsync(segment.Number, destination, cancellationToken);
                    }
                    string playlist = session.CreateVodPlaylist(SegmentName);
                    await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"), playlist, new UTF8Encoding(false), cancellationToken);
                    count = session.Segments.Count;
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("HLS session buffers were not released.");
                }
            }, cancellationToken: cancellationToken);
            await Console.Out.WriteLineAsync($"HLS stream copy: {count} MPEG-TS segments and a VOD playlist. Live buffer bytes: 0.");
            return 0;
        }

        private static string SegmentName(int number) => $"segment-{number.ToString(CultureInfo.InvariantCulture)}.ts";
    }
}
