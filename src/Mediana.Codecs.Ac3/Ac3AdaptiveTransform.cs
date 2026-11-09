// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Ac3
{
    /// <summary>ETSI TS 102 366 E.2.4 six-block vector/GAQ decoding and inverse DCT.</summary>
    internal class Ac3AdaptiveTransform(MediaBufferAllocator allocator) : IDisposable
    {
        private readonly bool[] enabled = new bool[7], decoded = new bool[7];
        private readonly WritableMediaBuffer?[] storage = new WritableMediaBuffer?[7];
        private static readonly double[] Transform = CreateTransform();
        public bool IsEnabled(int channel) => enabled[channel];
        public void Enable(int channel) => enabled[channel] = true;
        public void ResetFrame() { Array.Clear(enabled); Array.Clear(decoded); }

        public void ReadSpectrum(ref Ac3BitReader bits, int channel, int block, int start, int finish,
            ReadOnlySpan<byte> allocation, ReadOnlySpan<byte> exponents, Span<double> spectrum, ref uint random)
        {
            storage[channel] ??= allocator.Allocate(6 * 256 * sizeof(double));
            Span<double> values = MemoryMarshal.Cast<byte, double>(storage[channel]!.Span);
            if (!decoded[channel])
            {
                int mode = bits.Read(2);
                Span<byte> gains = stackalloc byte[256];
                ReadGains(ref bits, allocation, start, finish, mode, gains);
                Span<double> vector = stackalloc double[6];
                for (int bin = start; bin < finish; bin++)
                {
                    int bap = allocation[bin];
                    if (bap == 0)
                    {
                        for (int item = 0; item < 6; item++) { vector[item] = Ac3NoiseGenerator.Next(ref random) * .5; }
                    }
                    else if (bap <= 7)
                    {
                        int index = bits.Read(Ac3AhtQuantizer.Bits(bap));
                        ReadOnlySpan<short> entry = Ac3AhtVectorTables.Get(bap).Slice(index * 6, 6);
                        for (int item = 0; item < 6; item++) { vector[item] = entry[item] / 32768.0; }
                    }
                    else
                    {
                        for (int item = 0; item < 6; item++) { vector[item] = Ac3AhtQuantizer.Read(ref bits, bap, gains[bin]); }
                    }
                    for (int output = 0; output < 3; output++)
                    {
                        int row = output * 6;
                        double even = vector[0] + vector[2] * Transform[row + 2] + vector[4] * Transform[row + 4];
                        double odd = vector[1] * Transform[row + 1] + vector[3] * Transform[row + 3] + vector[5] * Transform[row + 5];
                        values[output * 256 + bin] = Ac3MantissaReader.ApplyExponent(even + odd, exponents[bin]);
                        values[(5 - output) * 256 + bin] = Ac3MantissaReader.ApplyExponent(even - odd, exponents[bin]);
                    }
                }
                decoded[channel] = true;
            }
            spectrum.Clear(); values.Slice(block * 256 + start, finish - start).CopyTo(spectrum[start..]);
        }

        private static void ReadGains(ref Ac3BitReader bits, ReadOnlySpan<byte> allocation,
            int start, int finish, int mode, scoped Span<byte> gains)
        {
            gains.Clear();
            if (mode == 0) { return; }
            int end = mode == 1 ? 12 : 17, group = 0, remaining = 0;
            for (int bin = start; bin < finish; bin++)
            {
                if (allocation[bin] is < 8 || allocation[bin] >= end) { continue; }
                if (mode is 1 or 2) { gains[bin] = (byte)(bits.Read(1) * mode); }
                else
                {
                    if (remaining == 0)
                    {
                        group = bits.Read(5); remaining = 3;
                        if (group >= 27) { throw new InvalidDataException("The E-AC-3 grouped adaptive gain is reserved."); }
                    }
                    int divisor = remaining switch { 3 => 9, 2 => 3, 1 => 1, _ => throw new InvalidOperationException() };
                    gains[bin] = (byte)(group / divisor); group %= divisor; remaining--;
                }
            }
        }

        private static double[] CreateTransform()
        {
            double[] result = new double[36];
            for (int output = 0; output < 6; output++)
            {
                result[output * 6] = 1;
                for (int input = 1; input < 6; input++)
                { result[output * 6 + input] = Math.Sqrt(2) * Math.Cos(input * (2 * output + 1) * Math.PI / 12); }
            }
            return result;
        }

        public void Dispose()
        {
            foreach (WritableMediaBuffer? buffer in storage) { buffer?.Dispose(); }
        }
    }
}
