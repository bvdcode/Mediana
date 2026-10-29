// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacSynthesis
    {
        public static void Process(ReadOnlySpan<double> spectrum, int sequence, int shape, int previousShape,
            Span<double> overlap, Span<double> output, Span<double> time, Span<Complex> work,
            CancellationToken cancellationToken)
        {
            if (spectrum.Length != 1024 || overlap.Length != 1024 || output.Length != 1024 || time.Length != 2048)
            {
                throw new ArgumentException("AAC synthesis requires a complete 1024-sample channel.");
            }
            switch (sequence)
            {
                case 0:
                case 1:
                case 3:
                    AacImdct.Transform(spectrum, time, work, cancellationToken);
                    ApplyLongWindow(time, sequence, shape, previousShape);
                    break;
                case 2:
                    time.Clear();
                    Span<double> block = stackalloc double[256];
                    ReadOnlySpan<double> left = AacWindows.Get(128, previousShape);
                    ReadOnlySpan<double> current = AacWindows.Get(128, shape);
                    for (int window = 0; window < 8; window++)
                    {
                        AacImdct.Transform(spectrum.Slice(window * 128, 128), block, work, cancellationToken);
                        int start = 448 + window * 128;
                        for (int index = 0; index < 128; index++)
                        {
                            time[start + index] += block[index] * left[index];
                            time[start + 128 + index] += block[128 + index] * current[127 - index];
                        }
                        left = current;
                    }
                    break;
                default:
                    throw new InvalidDataException("The AAC window sequence is invalid.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < 1024; index++)
            {
                output[index] = (overlap[index] + time[index]) / 32768;
                overlap[index] = time[1024 + index];
            }
        }

        private static void ApplyLongWindow(Span<double> time, int sequence, int shape, int previousShape)
        {
            switch (sequence)
            {
                case 0:
                    ApplyWindow(time[..1024], AacWindows.Get(1024, previousShape));
                    ApplyReversedWindow(time[1024..], AacWindows.Get(1024, shape));
                    break;
                case 1:
                    ApplyWindow(time[..1024], AacWindows.Get(1024, previousShape));
                    ApplyReversedWindow(time.Slice(1472, 128), AacWindows.Get(128, shape));
                    time[1600..].Clear();
                    break;
                case 3:
                    time[..448].Clear();
                    ApplyWindow(time.Slice(448, 128), AacWindows.Get(128, previousShape));
                    ApplyReversedWindow(time[1024..], AacWindows.Get(1024, shape));
                    break;
                default:
                    throw new InvalidOperationException("The long AAC window sequence is invalid.");
            }
        }

        private static void ApplyWindow(Span<double> time, ReadOnlySpan<double> window)
        {
            for (int index = 0; index < time.Length; index++)
            {
                time[index] *= window[index];
            }
        }

        private static void ApplyReversedWindow(Span<double> time, ReadOnlySpan<double> window)
        {
            for (int index = 0; index < time.Length; index++)
            {
                time[index] *= window[window.Length - 1 - index];
            }
        }
    }
}
