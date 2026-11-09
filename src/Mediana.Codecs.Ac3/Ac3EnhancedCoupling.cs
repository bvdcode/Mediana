// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Ac3
{
    /// <summary>ETSI TS 102 366 E.2.5 banded amplitude reconstruction.</summary>
    internal class Ac3EnhancedCoupling(MediaBufferAllocator allocator) : IDisposable
    {
        private const int MaximumBands = 22;
        private readonly bool[] merge = new bool[MaximumBands];
        private readonly int[] boundaries = new int[MaximumBands + 1], validBands = new int[5];
        private WritableMediaBuffer? storage;
        private int bandCount;
        private static ReadOnlySpan<byte> SubbandStarts => [13, 19, 25, 31, 37, 49, 61, 73, 85, 97, 109, 121, 133, 145, 157, 169, 181, 193, 205, 217, 229, 241, 253];
        private static ReadOnlySpan<byte> DefaultMerge => [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1];
        public int BeginCode { get; private set; }
        public int StartBin { get; private set; }
        public int EndBin { get; private set; }

        public void ResetFrame()
        {
            Array.Clear(validBands);
            for (int band = 0; band < MaximumBands; band++) { merge[band] = DefaultMerge[band] != 0; }
        }

        public void ReadStrategy(ref Ac3BitReader bits, bool spectralExtension, int spectralBegin)
        {
            BeginCode = bits.Read(4);
            int begin = BeginCode < 3 ? BeginCode * 2 : BeginCode < 13 ? BeginCode + 2 : BeginCode * 2 - 10;
            int finish = spectralExtension ? spectralBegin < 6 ? spectralBegin + 5 : spectralBegin * 2 : bits.Read(4) + 7;
            if (begin >= finish || finish > MaximumBands)
            { throw new InvalidDataException("The E-AC-3 enhanced coupling frequency range is invalid."); }
            if (bits.Flag())
            { for (int band = Math.Max(9, begin + 1); band < finish; band++) { merge[band] = bits.Flag(); } }
            bandCount = 0;
            for (int band = begin; band < finish; band++)
            {
                if (band == begin || !merge[band]) { boundaries[bandCount++] = SubbandStarts[band]; }
            }
            boundaries[bandCount] = SubbandStarts[finish];
            StartBin = SubbandStarts[begin]; EndBin = SubbandStarts[finish];
            storage ??= allocator.Allocate(5 * MaximumBands * sizeof(double));
        }

        public void ReadCoordinates(ref Ac3BitReader bits, ReadOnlySpan<bool> coupled, Span<bool> valid, int channelCount)
        {
            bits.Skip(1);
            int first = -1;
            for (int channel = 0; channel < channelCount; channel++)
            {
                if (!coupled[channel]) { valid[channel] = false; continue; }
                if (first < 0) { first = channel; }
                bool present = !valid[channel] || bits.Flag();
                bool reserved = channel > first && (!valid[channel] || bits.Flag());
                if (present)
                {
                    Span<double> amplitudes = Amplitudes(channel);
                    for (int band = 0; band < bandCount; band++) { amplitudes[band] = DecodeAmplitude(bits.Read(5)); }
                    validBands[channel] = bandCount;
                }
                else if (bandCount > validBands[channel])
                { throw new InvalidDataException("E-AC-3 enhanced coupling coordinates cannot introduce undefined bands."); }
                if (reserved) { bits.Skip(9 * (bandCount - 1)); }
                if (channel > first) { bits.Skip(1); }
                valid[channel] = true;
            }
        }

        internal static double DecodeAmplitude(int code)
        {
            if ((uint)code > 31) { throw new ArgumentOutOfRangeException(nameof(code)); }
            if (code == 31) { return 0; }
            if (code == 0) { return 1; }
            ReadOnlySpan<byte> mantissas = [27, 23, 19, 16];
            return Math.ScaleB(mantissas[(code - 1) % 4] / 32.0, -(code - 1) / 4);
        }

        public void Process(int channel, Span<double> spectrum, ReadOnlySpan<double> common, ReadOnlySpan<byte> allocation,
            ReadOnlySpan<byte> exponents, bool dither, ref uint random)
        {
            ReadOnlySpan<double> amplitudes = Amplitudes(channel);
            for (int band = 0; band < bandCount; band++)
            {
                double amplitude = amplitudes[band];
                for (int bin = boundaries[band]; bin < boundaries[band + 1]; bin++)
                {
                    double coefficient = common[bin];
                    if (allocation[bin] == 0 && dither)
                    { coefficient = Ac3MantissaReader.ApplyExponent(Ac3NoiseGenerator.Next(ref random) * 0.7071067811865476, exponents[bin]); }
                    spectrum[bin] = coefficient * amplitude;
                }
            }
        }

        private Span<double> Amplitudes(int channel) => MemoryMarshal.Cast<byte, double>(storage!.Span).Slice(channel * MaximumBands, MaximumBands);
        public void Dispose() => storage?.Dispose();
    }
}
