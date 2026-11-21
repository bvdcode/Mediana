// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal static class DtsSubframeHeader
    {
        public static (int Subsubframes, int PartialSamples) Read(ref DtsBitReader bits, DtsFrameHeader header,
            DtsChannelCoding[] channels, int count)
        {
            int groups = bits.Read(2) + 1;
            int partial = bits.Read(3);
            for (int ch = 0; ch < count; ch++)
            {
                for (int band = 0; band < channels[ch].ActiveBands; band++) { channels[ch].Predictors[band] = bits.Read(1) == 0 ? -1 : 0; }
            }
            for (int ch = 0; ch < count; ch++)
            {
                for (int band = 0; band < channels[ch].ActiveBands; band++)
                {
                    if (channels[ch].Predictors[band] >= 0) { channels[ch].Predictors[band] = bits.Read(12); }
                }
            }
            for (int ch = 0; ch < count; ch++)
            {
                DtsChannelCoding channel = channels[ch];
                for (int band = 0; band < channel.VectorStart; band++)
                {
                    int allocation = channel.AllocationCodebook switch
                    {
                        < 5 => DtsCodebooks.Allocation[channel.AllocationCodebook].Read(ref bits),
                        5 => bits.Read(4),
                        6 => bits.Read(5),
                        _ => throw new InvalidDataException("The DTS allocation selector is reserved.")
                    };
                    if (allocation > 26) { throw new InvalidDataException("The DTS bit allocation is reserved."); }
                    channel.Allocations[band] = allocation;
                }
                Array.Clear(channel.Transients);
            }
            if (groups > 1)
            {
                for (int ch = 0; ch < count; ch++)
                {
                    DtsChannelCoding channel = channels[ch];
                    for (int band = 0; band < channel.VectorStart; band++)
                    {
                        if (channel.Allocations[band] != 0)
                        {
                            int transient = DtsCodebooks.Transient[channel.TransientCodebook].Read(ref bits);
                            if (transient >= groups) { throw new InvalidDataException("The DTS transient lies outside its subframe."); }
                            channel.Transients[band] = transient;
                        }
                    }
                }
            }
            for (int ch = 0; ch < count; ch++)
            {
                DtsChannelCoding channel = channels[ch];
                int sum = 0;
                for (int band = 0; band < channel.ActiveBands; band++)
                {
                    channel.BeforeScales[band] = channel.AfterScales[band] = 0;
                    if (band < channel.VectorStart && channel.Allocations[band] == 0) { continue; }
                    channel.BeforeScales[band] = Scale(ref bits, channel.ScaleCodebook, ref sum);
                    if (channel.Transients[band] != 0) { channel.AfterScales[band] = Scale(ref bits, channel.ScaleCodebook, ref sum); }
                }
            }
            for (int ch = 0; ch < count; ch++)
            {
                if (channels[ch].JointSource >= 0)
                {
                    channels[ch].JointCodebook = bits.Read(3);
                    if (channels[ch].JointCodebook == 7) { throw new InvalidDataException("The DTS joint scale selector is reserved."); }
                }
            }
            for (int ch = 0; ch < count; ch++)
            {
                DtsChannelCoding channel = channels[ch];
                if (channel.JointSource < 0) { continue; }
                for (int band = channel.ActiveBands; band < channels[channel.JointSource].ActiveBands; band++)
                {
                    int index = ReadScaleIndex(ref bits, channel.JointCodebook) + 64;
                    if ((uint)index >= 128) { throw new InvalidDataException("The DTS joint scale index is invalid."); }
                    channel.JointScales[band] = DtsQuantizationTables.JointScale[index];
                }
            }
            if (header.HasDynamicRange) { bits.Skip(8); }
            if (header.HasCrc) { bits.Skip(16); }
            return (groups, partial);
        }

        private static double Scale(ref DtsBitReader bits, int selector, ref int sum)
        {
            int value = ReadScaleIndex(ref bits, selector);
            sum = selector < 5 ? sum + value : value;
            ReadOnlySpan<int> table = selector == 6 ? DtsQuantizationTables.Scale7 : DtsQuantizationTables.Scale6;
            if ((uint)sum >= table.Length || table[sum] == 0) { throw new InvalidDataException("The DTS scale index is invalid."); }
            return table[sum];
        }

        private static int ReadScaleIndex(ref DtsBitReader bits, int selector) => selector switch
        {
            < 5 => DtsCodebooks.Scale[selector].Read(ref bits),
            5 => bits.Read(6),
            6 => bits.Read(7),
            _ => throw new InvalidDataException("The DTS scale selector is reserved.")
        };
    }
}
