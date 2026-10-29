// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class Mp4HlsAudioIndex
    {
        public static Mp4HlsAudioRange[] Create(Mp4Reader reader, int streamIndex,
            IReadOnlyList<Mp4AvcSegment> video, CancellationToken cancellationToken)
        {
            int count = reader.Tracks[streamIndex].SampleCount;
            if (count == 0)
            {
                throw new NotSupportedException("The selected AAC track must contain samples.");
            }
            Mp4HlsAudioRange[] ranges = new Mp4HlsAudioRange[video.Count];
            int sample = 0;
            MediaTimestamp? previous = null;
            for (int number = 0; number < ranges.Length; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MediaTimestamp? boundary = number + 1 < video.Count ? video[number + 1].DecodeStart : null;
                int first = sample;
                MediaTimestamp? start = null;
                MediaTimestamp? end = null;
                while (sample < count)
                {
                    Mp4SampleInfo info = reader.GetSampleInfo(streamIndex, sample, cancellationToken);
                    if (boundary.HasValue && info.DecodeTime.CompareTo(boundary.Value) >= 0)
                    {
                        break;
                    }
                    if (info.Duration.Value == 0 || info.PresentationTime != info.DecodeTime ||
                        (previous.HasValue && info.DecodeTime.CompareTo(previous.Value) <= 0))
                    {
                        throw new NotSupportedException("AAC coverage requires positive durations, increasing DTS and PTS equal to DTS.");
                    }
                    start ??= info.PresentationTime;
                    MediaTimestamp sampleEnd = new(checked(info.PresentationTime.Value + info.Duration.Value), info.Duration.TimeBase);
                    if (!end.HasValue || sampleEnd.CompareTo(end.Value) > 0)
                    {
                        end = sampleEnd;
                    }
                    previous = info.DecodeTime;
                    sample++;
                }
                ranges[number] = new(streamIndex, first, sample - first, start, end);
            }
            return ranges;
        }
    }
}
