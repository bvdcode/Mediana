// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class HlsClock
    {
        public static TimeBase Common(TimeBase video, TimeBase audio)
        {
            if (video == audio)
            {
                return video;
            }
            int a = video.Denominator;
            int b = audio.Denominator;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            long denominator = (long)(video.Denominator / a) * audio.Denominator;
            if (denominator > int.MaxValue)
            {
                throw new NotSupportedException("The audio and video clocks require an HLS time base exceeding the supported denominator.");
            }
            return new(1, (int)denominator);
        }
    }
}
