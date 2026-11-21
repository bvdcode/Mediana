// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal static class DtsCodingHeader
    {
        public static int Read(ref DtsBitReader bits, DtsFrameHeader header, DtsChannelCoding[] channels, int channelCount)
        {
            int subframes = bits.Read(4) + 1;
            if (bits.Read(3) + 1 != channelCount) { throw new InvalidDataException("DTS primary channel count disagrees with its arrangement."); }
            for (int ch = 0; ch < channelCount; ch++)
            {
                channels[ch].ActiveBands = bits.Read(5) + 2;
                if (channels[ch].ActiveBands > 32) { throw new InvalidDataException("DTS subband activity exceeds 32 bands."); }
            }
            for (int ch = 0; ch < channelCount; ch++)
            {
                channels[ch].VectorStart = bits.Read(5) + 1;
                if (channels[ch].VectorStart > channels[ch].ActiveBands) { throw new InvalidDataException("DTS vector start exceeds the active bands."); }
            }
            for (int ch = 0; ch < channelCount; ch++)
            {
                int source = bits.Read(3) - 1;
                if (source >= channelCount || source == ch) { throw new InvalidDataException("The DTS joint coding channel is invalid."); }
                channels[ch].JointSource = source;
            }
            for (int ch = 0; ch < channelCount; ch++) { channels[ch].TransientCodebook = bits.Read(2); }
            for (int ch = 0; ch < channelCount; ch++)
            {
                channels[ch].ScaleCodebook = bits.Read(3);
                if (channels[ch].ScaleCodebook == 7) { throw new InvalidDataException("The DTS scale codebook is reserved."); }
            }
            for (int ch = 0; ch < channelCount; ch++)
            {
                channels[ch].AllocationCodebook = bits.Read(3);
                if (channels[ch].AllocationCodebook == 7) { throw new InvalidDataException("The DTS allocation codebook is reserved."); }
                Array.Fill(channels[ch].Adjustments, 1);
            }
            for (int allocation = 1; allocation <= 10; allocation++)
            {
                int width = allocation == 1 ? 1 : allocation <= 5 ? 2 : 3;
                for (int ch = 0; ch < channelCount; ch++) { channels[ch].Selectors[allocation] = bits.Read(width); }
            }
            for (int allocation = 1; allocation <= 10; allocation++)
            {
                for (int ch = 0; ch < channelCount; ch++)
                {
                    if (channels[ch].Selectors[allocation] < DtsCodebooks.Samples[allocation].Length)
                    {
                        channels[ch].Adjustments[allocation] = bits.Read(2) switch
                        {
                            0 => 1,
                            1 => 1.125,
                            2 => 1.25,
                            3 => 1.4375,
                            _ => throw new InvalidOperationException()
                        };
                    }
                }
            }
            if (header.HasCrc) { bits.Skip(16); }
            return subframes;
        }
    }
}
