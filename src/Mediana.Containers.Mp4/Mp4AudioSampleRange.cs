// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    /// <summary>Decoded PCM sample selection for one unit-rate media edit, optionally preceded by an empty edit.
    /// Sample indices are relative to media time zero. Selection includes sample instants in the half-open edit interval.
    /// This interprets explicit edits only; it does not infer codec delay, sample-group roll dependencies or silence.</summary>
    public readonly record struct Mp4AudioSampleRange(long StartSample, long SampleCount, MediaTimestamp PresentationTime)
    {
        /// <summary>The caller supplies the decoded PCM rate and decodes preceding packets for codec state.
        /// PresentationTime preserves any leading empty-edit delay without generating silent PCM.</summary>
        public static Mp4AudioSampleRange FromEditList(Mp4TrackInfo track, int sampleRate)
        {
            ArgumentNullException.ThrowIfNull(track);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            if (track.Stream.Kind != MediaKind.Audio || track.Edits.Count == 0)
            {
                throw new NotSupportedException("Audio sample selection requires an audio track with an explicit edit list.");
            }
            Mp4LinearTimeline timeline = Mp4LinearTimeline.Create(track.Stream.TimeBase, track.Edits);
            Mp4Edit edit = track.Edits[^1];
            int denominator = LeastCommonMultiple(LeastCommonMultiple(sampleRate, timeline.TimeBase.Denominator), edit.Duration.TimeBase.Denominator);
            TimeBase timeBase = new(1, denominator);
            MediaTimestamp movieStart = timeline.Map(edit.MediaTime).Rescale(timeBase, TimestampRounding.TowardZero);
            Int128 start = checked((Int128)edit.MediaTime * track.Stream.TimeBase.Numerator * (denominator / track.Stream.TimeBase.Denominator));
            Int128 duration = checked((Int128)edit.Duration.Value * edit.Duration.TimeBase.Numerator * (denominator / edit.Duration.TimeBase.Denominator));
            long firstSample = Ceiling(checked(start * sampleRate), denominator);
            long endSample = Ceiling(checked((start + duration) * sampleRate), denominator);
            Int128 presentation = checked((Int128)movieStart.Value + (Int128)firstSample * (denominator / sampleRate) - start);
            return new(firstSample, checked(endSample - firstSample), new MediaTimestamp(checked((long)presentation), timeBase));
        }

        private static long Ceiling(Int128 numerator, int denominator)
        {
            Int128 result = numerator / denominator;
            if (numerator % denominator != 0)
            {
                result++;
            }
            return checked((long)result);
        }

        private static int LeastCommonMultiple(int left, int right)
        {
            int a = left;
            int b = right;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            long denominator = (long)(left / a) * right;
            if (denominator > int.MaxValue)
            {
                throw new NotSupportedException("The audio edit and sample rate cannot share an exact supported time base.");
            }
            return (int)denominator;
        }
    }
}
