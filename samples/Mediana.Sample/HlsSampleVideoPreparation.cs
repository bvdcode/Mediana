// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal static class HlsSampleVideoPreparation
    {
        public static async Task<HlsVariant> RunAsync(string input, string directory,
            PreparedHlsSampleOptions options, CancellationToken token)
        {
            Directory.CreateDirectory(directory);
            List<MpegTsSegmentState?> states = [];
            HlsPreparedVideo prepared = await HlsSampleVideoEncoding.RunAsync<AvcHlsWriter, HlsPreparedVideo>(input,
                options.Start, options.End, options,
                (configuration, allocator) => AvcHlsWriter.Create(configuration, (number, state, cancellationToken) =>
                {
                    if (number != states.Count)
                    {
                        throw new InvalidOperationException("Video preparation changed its segment numbering.");
                    }
                    states.Add(state);
                    return PreparedHlsSample.OpenSegmentAsync(directory, $"prepared-{number}.ts", cancellationToken);
                }, allocator, new()
                {
                    TargetDuration = options.TargetDuration,
                    MaximumSegments = options.MaximumSegments,
                    PlaylistTargetDurationSeconds = options.TargetSeconds
                }),
                (writer, configuration, format) => new(configuration,
                    writer.CreateVariant("video/index.m3u8",
                        format.SampleAspectRatio == SampleAspectRatio.Square ? format.Width : null,
                        format.SampleAspectRatio == SampleAspectRatio.Square ? format.Height : null),
                    Array.AsReadOnly(writer.Segments.ToArray()), Array.AsReadOnly(states.ToArray())), token);
            HlsMediaSegment[] requested = prepared.Segments.ToArray();
            for (int number = requested.Length - 1; number >= 0; number--)
            {
                long bytes = await RequestAsync(input, directory, $"{number}.ts", number, prepared, options, token);
                requested[number] = requested[number] with { ByteLength = bytes };
            }
            int last = requested.Length - 1;
            await RequestAsync(input, directory, $"repeat-{last}.ts", last, prepared, options, token);
            await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"),
                HlsMediaPlaylist.Create(requested, number => $"{number}.ts", independentSegments: true,
                    targetDurationSeconds: options.TargetSeconds), token);
            (long peak, long average) = HlsMediaPlaylist.MeasureBandwidth(requested, options.TargetSeconds);
            await Console.Out.WriteLineAsync($"Prepared {requested.Length} video boundaries; regenerated segments in reverse order and repeated segment {last}.");
            return prepared.Variant with { Bandwidth = peak, AverageBandwidth = average, AudioGroupId = "audio" };
        }

        private static async Task<long> RequestAsync(string input, string directory, string name, int number,
            HlsPreparedVideo prepared, PreparedHlsSampleOptions options, CancellationToken token)
        {
            HlsMediaSegment segment = prepared.Segments[number];
            MediaTimestamp end = new(checked(segment.Start.Value + segment.Duration.Value), segment.Start.TimeBase);
            await using FileStream destination = PreparedHlsSample.OpenDestination(directory, name);
            return await HlsSampleVideoEncoding.RunAsync<MpegTsWriter, long>(input, segment.Start, end, options,
                (configuration, allocator) =>
                {
                    EncodedStreamInfo original = prepared.Configuration;
                    if (configuration.TimeBase != original.TimeBase ||
                        !configuration.Configuration.Span.SequenceEqual(original.Configuration.Span))
                    {
                        throw new InvalidDataException("Independent video encoding changed its stream configuration.");
                    }
                    MpegTsSegmentState? state = prepared.States[number];
                    return state is null ? MpegTsWriter.Create(destination, [original], allocator)
                        : MpegTsWriter.ResumeSegment(destination, state, allocator);
                }, (writer, _, _) => writer.BytesWritten, token);
        }
    }
}
