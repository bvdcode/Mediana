// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal static class VorbisSpectrum
    {
        public static void Decode(ref VorbisBitReader reader, VorbisSetupCapture setup, VorbisMode mode,
            int bins, Span<float> spectrum, Span<float> floor, Span<float> vector, Span<byte> classes, CancellationToken token)
        {
            VorbisMapping mapping = setup.Mappings[mode.Mapping];
            int channels = mapping.Multiplex.Length;
            spectrum = spectrum[..checked(channels * bins)]; spectrum.Clear();
            Span<bool> nonzero = stackalloc bool[255];
            for (int channel = 0; channel < channels; channel++)
            {
                int submap = mapping.Multiplex[channel];
                nonzero[channel] = setup.Floors[mapping.Floors[submap]].Decode(ref reader, setup.Books,
                    floor.Slice(channel * bins, bins), vector, token);
            }
            for (int step = 0; step < mapping.CouplingSteps; step++)
            {
                int magnitude = mapping.Magnitudes[step], angle = mapping.Angles[step];
                if (nonzero[magnitude] || nonzero[angle]) { nonzero[magnitude] = true; nonzero[angle] = true; }
            }
            Span<int> selected = stackalloc int[255];
            for (int submap = 0; submap < mapping.SubmapCount; submap++)
            {
                VorbisResidue residue = setup.Residues[mapping.Residues[submap]];
                int count = 0; bool active = false;
                for (int channel = 0; channel < channels; channel++)
                {
                    if (mapping.Multiplex[channel] != submap) { continue; }
                    active |= nonzero[channel];
                    if (residue.IsInterleaved || nonzero[channel]) { selected[count++] = channel; }
                }
                if (active) { residue.Decode(ref reader, setup.Books, spectrum, bins, bins, selected[..count], classes, vector, token); }
            }
            for (int step = mapping.CouplingSteps - 1; step >= 0; step--)
            {
                Span<float> magnitudes = spectrum.Slice(mapping.Magnitudes[step] * bins, bins);
                Span<float> angles = spectrum.Slice(mapping.Angles[step] * bins, bins);
                for (int index = 0; index < bins; index++)
                {
                    float magnitude = magnitudes[index], angle = angles[index];
                    if (magnitude > 0)
                    {
                        if (angle > 0) { magnitudes[index] = magnitude; angles[index] = magnitude - angle; }
                        else { magnitudes[index] = magnitude + angle; angles[index] = magnitude; }
                    }
                    else
                    {
                        if (angle > 0) { magnitudes[index] = magnitude; angles[index] = magnitude + angle; }
                        else { magnitudes[index] = magnitude - angle; angles[index] = magnitude; }
                    }
                }
            }
            Multiply(spectrum, floor[..spectrum.Length]);
        }

        private static void Multiply(Span<float> output, ReadOnlySpan<float> scale)
        {
            int index = 0;
            if (Vector.IsHardwareAccelerated)
            {
                for (; index <= output.Length - Vector<float>.Count; index += Vector<float>.Count)
                {
                    (new Vector<float>(output[index..]) * new Vector<float>(scale[index..])).CopyTo(output[index..]);
                }
            }
            for (; index < output.Length; index++) { output[index] *= scale[index]; }
        }
    }
}
