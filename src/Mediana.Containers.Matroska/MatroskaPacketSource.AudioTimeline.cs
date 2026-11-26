// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaPacketSource
    {
        private void InitializeAudioTimeline(CancellationToken token)
        {
            if (audioTiming is null)
            {
                return;
            }
            for (int stream = 0; stream < tracks.Length; stream++)
            {
                if (!audioTiming.HasStream(stream))
                {
                    continue;
                }
                MediaTimestamp? next = null;
                MediaTimestamp? independent = null;
                MatroskaCodecConfiguration configuration = configurations[stream];
                int previousBlock = -1;
                long count = index.GetPacketCount(stream);
                for (long packet = 0; packet < count; packet++)
                {
                    token.ThrowIfCancellationRequested();
                    MatroskaPacketAddress address = index.Locate(stream, packet);
                    MatroskaPacketTiming timing = GetTiming(address);
                    MatroskaAudioFrameTiming audio = audioTiming.Get(stream, packet)!.Value;
                    MediaTimestamp timestamp = timing.PresentationTime!.Value;
                    MediaTimestamp? expected = audio.IsDependent ? independent : next;
                    if (expected is MediaTimestamp candidate && (address.BlockIndex == previousBlock ||
                        clocks[stream].MatchesTimestampPrecision(timestamp, candidate)))
                    {
                        timestamp = candidate;
                    }
                    audioTiming.SetPresentationTime(stream, packet, timestamp);
                    if (!audio.IsDependent && audio.Samples != 0)
                    {
                        independent = timestamp;
                        MediaDuration period = MatroskaAudioPacketSamples.HasNativeSampleCounts(configuration.Stream.Format)
                            ? new(audio.Samples, new(1, audio.SampleRate))
                            : configuration.FrameDuration ?? timing.Duration!.Value;
                        next = AddExactDuration(timestamp, period);
                    }
                    previousBlock = address.BlockIndex;
                }
            }
        }

        private static MediaTimestamp? AddExactDuration(MediaTimestamp timestamp, MediaDuration duration)
        {
            MediaTimestamp start = timestamp.ReduceClock();
            MediaTimestamp length = new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock();
            int a = start.TimeBase.Denominator;
            int b = length.TimeBase.Denominator;
            int divisor = a;
            int remainder = b;
            while (remainder != 0)
            {
                (divisor, remainder) = (remainder, divisor % remainder);
            }
            long denominator = (long)(a / divisor) * b;
            if (denominator > int.MaxValue)
            {
                return null;
            }
            Int128 value = (Int128)start.Value * start.TimeBase.Numerator * (denominator / a)
                + (Int128)length.Value * length.TimeBase.Numerator * (denominator / b);
            return new(checked((long)value), new(1, (int)denominator));
        }
    }
}
