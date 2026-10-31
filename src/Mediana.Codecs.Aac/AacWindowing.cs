// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacWindowing
    {
        public static void ApplyLong(Span<double> time, int sequence, int shape, int previousShape)
        {
            if (time.Length != 2048)
            {
                throw new ArgumentException("AAC long windowing requires 2048 time-domain samples.", nameof(time));
            }
            switch (sequence)
            {
                case 0:
                    Apply(time[..1024], AacWindows.Get(1024, previousShape));
                    ApplyReversed(time[1024..], AacWindows.Get(1024, shape));
                    break;
                case 1:
                    Apply(time[..1024], AacWindows.Get(1024, previousShape));
                    ApplyReversed(time.Slice(1472, 128), AacWindows.Get(128, shape));
                    time[1600..].Clear();
                    break;
                case 3:
                    time[..448].Clear();
                    Apply(time.Slice(448, 128), AacWindows.Get(128, previousShape));
                    ApplyReversed(time[1024..], AacWindows.Get(1024, shape));
                    break;
                default:
                    throw new InvalidOperationException("The long AAC window sequence is invalid.");
            }
        }

        private static void Apply(Span<double> time, ReadOnlySpan<double> window)
        {
            for (int index = 0; index < time.Length; index++)
            {
                time[index] *= window[index];
            }
        }

        private static void ApplyReversed(Span<double> time, ReadOnlySpan<double> window)
        {
            for (int index = 0; index < time.Length; index++)
            {
                time[index] *= window[window.Length - 1 - index];
            }
        }
    }
}
