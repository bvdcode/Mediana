// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Ac3
{
    /// <summary>ETSI TS 102 366 E.2.6 spectral translation, noise blending and envelope reconstruction.</summary>
    internal class Ac3SpectralExtension(MediaBufferAllocator allocator) : IDisposable
    {
        private const int MaximumBands = 17;
        private readonly bool[] channels = new bool[5], coordinates = new bool[5], merge = new bool[MaximumBands];
        private readonly int[] bandSizes = new int[MaximumBands], validBands = new int[5], attenuation = new int[5];
        private static readonly double[] AttenuationTable = CreateAttenuation();
        private WritableMediaBuffer? storage;
        private int copyStart, endBin, bandCount;
        public bool InUse { get; private set; }
        public int BeginBin { get; private set; }
        public int BeginCode { get; private set; }
        public bool UsesChannel(int channel) => channels[channel];

        public void ResetFrame()
        {
            InUse = false;
            Array.Clear(channels); Array.Clear(coordinates); Array.Clear(validBands);
            attenuation.AsSpan().Fill(-1);
            for (int band = 0; band < MaximumBands; band++) { merge[band] = band >= 8 && (band & 1) == 0; }
        }

        public void ReadAttenuation(ref Ac3BitReader bits, int channelCount)
        {
            for (int channel = 0; channel < channelCount; channel++)
            { attenuation[channel] = bits.Flag() ? bits.Read(5) : -1; }
        }

        public void ReadBlock(ref Ac3BitReader bits, int block, int channelCount, bool mono)
        {
            if (block == 0 || bits.Flag())
            {
                InUse = bits.Flag();
                if (!InUse) { Array.Clear(channels); Array.Clear(coordinates); }
                else
                {
                    for (int channel = 0; channel < channelCount; channel++) { channels[channel] = mono || bits.Flag(); }
                    copyStart = 25 + bits.Read(2) * 12;
                    BeginCode = bits.Read(3);
                    int finish = bits.Read(3);
                    int beginBand = BeginCode < 6 ? BeginCode + 2 : BeginCode * 2 - 3;
                    int endBand = finish < 3 ? finish + 5 : finish * 2 + 3;
                    BeginBin = 25 + beginBand * 12; endBin = 25 + endBand * 12;
                    if (copyStart >= BeginBin || beginBand >= endBand)
                    { throw new InvalidDataException("The E-AC-3 spectral extension frequency range is invalid."); }
                    if (bits.Flag())
                    { for (int band = beginBand + 1; band < endBand; band++) { merge[band] = bits.Flag(); } }
                    bandCount = 1; bandSizes[0] = 12;
                    for (int band = beginBand + 1; band < endBand; band++)
                    {
                        if (merge[band]) { bandSizes[bandCount - 1] += 12; }
                        else { bandSizes[bandCount++] = 12; }
                    }
                    storage ??= allocator.Allocate(5 * MaximumBands * 2 * sizeof(double));
                }
            }
            if (!InUse) { return; }
            for (int channel = 0; channel < channelCount; channel++)
            {
                if (!channels[channel]) { coordinates[channel] = false; continue; }
                if (!coordinates[channel] || bits.Flag())
                {
                    double blend = bits.Read(5) / 32.0;
                    int master = bits.Read(2) * 3, bin = BeginBin;
                    Span<double> gains = Gains(channel);
                    for (int band = 0; band < bandCount; band++)
                    {
                        int exponent = bits.Read(4), mantissa = bits.Read(2);
                        double coordinate = Math.ScaleB(exponent == 15 ? mantissa / 4.0 : (mantissa + 4) / 8.0,
                            -exponent - master) * 32;
                        double ratio = Math.Clamp((bin + bandSizes[band] * .5) / endBin - blend, 0, 1);
                        gains[band * 2] = coordinate * Math.Sqrt(1 - ratio);
                        gains[band * 2 + 1] = coordinate * Math.Sqrt(ratio);
                        bin += bandSizes[band];
                    }
                    coordinates[channel] = true; validBands[channel] = bandCount;
                }
                else if (bandCount > validBands[channel])
                { throw new InvalidDataException("E-AC-3 spectral extension coordinates cannot introduce undefined bands."); }
            }
        }

        public void Process(int channel, Span<double> spectrum, ref uint random)
        {
            if (!InUse || !channels[channel]) { return; }
            Span<bool> wrapped = stackalloc bool[MaximumBands];
            Span<double> energy = stackalloc double[MaximumBands];
            int source = copyStart, target = BeginBin;
            for (int band = 0; band < bandCount; band++)
            {
                int size = bandSizes[band];
                wrapped[band] = source + size > BeginBin;
                if (wrapped[band]) { source = copyStart; }
                double sum = 0;
                for (int bin = 0; bin < size; bin++)
                {
                    if (source == BeginBin) { source = copyStart; }
                    double value = spectrum[source++]; spectrum[target++] = value; sum += value * value;
                }
                energy[band] = Math.Sqrt(sum / size);
            }
            if (attenuation[channel] >= 0)
            {
                ApplyNotch(spectrum, BeginBin, attenuation[channel]);
                int boundary = BeginBin + bandSizes[0];
                for (int band = 1; band < bandCount; band++)
                {
                    if (wrapped[band]) { ApplyNotch(spectrum, boundary, attenuation[channel]); }
                    boundary += bandSizes[band];
                }
            }
            ReadOnlySpan<double> gains = Gains(channel);
            target = BeginBin;
            for (int band = 0; band < bandCount; band++)
            {
                double signalGain = gains[band * 2], noiseGain = energy[band] * gains[band * 2 + 1];
                for (int bin = 0; bin < bandSizes[band]; bin++, target++)
                {
                    double noise = Ac3NoiseGenerator.Next(ref random) * 1.7320508075688772;
                    spectrum[target] = spectrum[target] * signalGain + noise * noiseGain;
                }
            }
        }

        private Span<double> Gains(int channel) => MemoryMarshal.Cast<byte, double>(storage!.Span)
            .Slice(channel * MaximumBands * 2, MaximumBands * 2);

        private static void ApplyNotch(Span<double> spectrum, int boundary, int code)
        {
            ReadOnlySpan<double> filter = AttenuationTable.AsSpan(code * 3, 3);
            spectrum[boundary - 2] *= filter[0]; spectrum[boundary - 1] *= filter[1];
            spectrum[boundary] *= filter[2]; spectrum[boundary + 1] *= filter[1]; spectrum[boundary + 2] *= filter[0];
        }

        private static double[] CreateAttenuation()
        {
            double[] result = new double[32 * 3];
            for (int code = 0; code < 32; code++)
            { for (int bin = 0; bin < 3; bin++) { result[code * 3 + bin] = Math.Pow(2, -(code + 1) * (bin + 1) / 15.0); } }
            return result;
        }

        public void Dispose() => storage?.Dispose();
    }
}
