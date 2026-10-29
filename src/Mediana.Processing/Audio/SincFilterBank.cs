using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    internal class SincFilterBank(int radius, int phases, WritableMediaBuffer coefficients) : IDisposable
    {
        public int Radius { get; } = radius;
        public int Phases { get; } = phases;
        public int Taps => 2 * Radius + 1;

        public ReadOnlySpan<double> GetPhase(int phase) =>
            MemoryMarshal.Cast<byte, double>(coefficients.Memory.Span).Slice(phase * Taps, Taps);

        public static SincFilterBank Create(int inputRate, int outputRate, AudioResampleOptions options,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            int phases = outputRate / AudioSampleClock.GreatestCommonDivisor(inputRate, outputRate);
            double cutoff = Math.Min(1, (double)outputRate / inputRate) * options.CutoffRatio;
            double support = Math.Ceiling(options.FilterHalfLength / cutoff);
            if (inputRate == outputRate)
            {
                support = 0;
            }
            if (support > (int.MaxValue - 1) / 2)
            {
                throw new NotSupportedException("The resampling filter exceeds supported dimensions.");
            }
            int radius = (int)support;
            int taps = 2 * radius + 1;
            Int128 bytes = (Int128)phases * taps * sizeof(double);
            if (bytes > options.MaxCoefficientBytes)
            {
                throw new NotSupportedException("The resampling filter exceeds the configured coefficient limit.");
            }
            WritableMediaBuffer storage = allocator.Allocate((int)bytes);
            bool created = false;
            try
            {
                Span<double> table = MemoryMarshal.Cast<byte, double>(storage.Memory.Span);
                for (int phase = 0; phase < phases; phase++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double fraction = (double)phase / phases;
                    double sum = 0;
                    for (int tap = 0; tap < taps; tap++)
                    {
                        if ((tap & 1023) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        double weight;
                        if (radius == 0)
                        {
                            weight = 1;
                        }
                        else
                        {
                            double distance = tap - radius - fraction;
                            double windowPosition = distance / radius;
                            weight = 0;
                            if (Math.Abs(windowPosition) < 1)
                            {
                                double window = 0.42 + 0.5 * Math.Cos(Math.PI * windowPosition)
                                    + 0.08 * Math.Cos(2 * Math.PI * windowPosition);
                                double sinc = distance == 0 ? cutoff : Math.Sin(Math.PI * cutoff * distance) / (Math.PI * distance);
                                weight = sinc * window;
                            }
                        }
                        table[phase * taps + tap] = weight;
                        sum += weight;
                    }
                    for (int tap = 0; tap < taps; tap++)
                    {
                        table[phase * taps + tap] /= sum;
                    }
                }
                SincFilterBank result = new(radius, phases, storage);
                created = true;
                return result;
            }
            finally
            {
                if (!created)
                {
                    storage.Dispose();
                }
            }
        }

        public void Dispose() => coefficients.Dispose();
    }
}
