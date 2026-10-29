// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing.Audio
{
    internal static class AudioBoundaryExtension
    {
        public static long Resolve(long sample, long length, AudioBoundaryMode mode)
        {
            if (sample >= 0 && sample < length)
            {
                return sample;
            }
            switch (mode)
            {
                case AudioBoundaryMode.Zero:
                    return -1;
                case AudioBoundaryMode.Reflect:
                    if (length == 1)
                    {
                        return 0;
                    }
                    Int128 period = 2 * ((Int128)length - 1);
                    Int128 position = sample % period;
                    if (position < 0)
                    {
                        position += period;
                    }
                    if (position >= length)
                    {
                        position = period - position;
                    }
                    return (long)position;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
