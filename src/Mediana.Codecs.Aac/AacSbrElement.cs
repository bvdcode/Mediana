// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacSbrElement(int channelCount, int outputSampleRate)
    {
        public AacSbrHeader? Header { get; private set; }
        public AacSbrFrequencyTables Tables { get; } = new();
        public AacSbrPatches Patches { get; } = new();
        public AacSbrChannelData[] Channels { get; } = Enumerable.Range(0, channelCount).Select(_ => new AacSbrChannelData()).ToArray();
        public bool FrequencyReset { get; private set; }
        public bool Present { get; set; }
        public AacPsData? ParametricStereo { get; private set; }
        public bool HasParametricStereoData { get; private set; }

        public static bool InspectRandomAccess(ref AacBitReader bits, bool crc, int channelCount, int outputSampleRate, out bool hasParametricStereo)
        {
            hasParametricStereo = false;
            if (crc)
            {
                VerifyCrc(ref bits);
            }
            AacBitReader header = bits;
            if (header.Read(1) == 0)
            {
                bits.Skip(bits.Remaining);
                return false;
            }
            AacSbrElement element = new(channelCount, outputSampleRate);
            element.Read(ref bits, false);
            hasParametricStereo = element.HasParametricStereoData;
            return element.ParametricStereo is null || element.ParametricStereo.Independent;
        }

        public void Read(ref AacBitReader bits, bool crc)
        {
            if (Present)
            {
                throw new InvalidDataException("An AAC channel element has more than one SBR payload.");
            }
            Present = true;
            HasParametricStereoData = false;
            if (crc)
            {
                VerifyCrc(ref bits);
            }
            FrequencyReset = false;
            bool headerPresent = bits.Read(1) != 0;
            if (headerPresent)
            {
                AacSbrHeader next = AacSbrHeader.Read(ref bits);
                FrequencyReset = Header is null || next.ChangesFrequencyTables(Header);
                if (FrequencyReset)
                {
                    Tables.Update(next, outputSampleRate);
                    Patches.Update(Tables, outputSampleRate);
                }
                Header = next;
            }
            AacSbrHeader header = Header ?? throw new InvalidDataException("SBR decoding requires a header from the beginning of the stream.");
            if (bits.Read(1) != 0)
            {
                bits.Skip(Channels.Length == 2 ? 8 : 4);
            }
            bool coupled = Channels.Length == 2 && bits.Read(1) != 0;
            AacSbrChannelData left = Channels[0];
            left.Grid.Read(ref bits);
            if (Channels.Length == 2)
            {
                if (coupled)
                {
                    Channels[1].Grid.CopyFrom(left.Grid);
                }
                else
                {
                    Channels[1].Grid.Read(ref bits);
                }
            }
            foreach (AacSbrChannelData channel in Channels)
            {
                channel.ReadDirections(ref bits);
                if (headerPresent && (channel.EnvelopeTimeDirection[0] || channel.NoiseTimeDirection[0]))
                {
                    throw new InvalidDataException("An SBR header frame must code its first envelope and noise floor independently.");
                }
            }
            left.ReadInverseFiltering(ref bits, Tables.NoiseCount);
            if (Channels.Length == 1)
            {
                AacSbrEnvelopeParser.ReadEnvelope(ref bits, left, Tables, header, false);
                AacSbrEnvelopeParser.ReadNoise(ref bits, left, Tables, false);
            }
            else
            {
                AacSbrChannelData right = Channels[1];
                if (coupled)
                {
                    left.InverseFiltering.CopyTo(right.InverseFiltering, 0);
                    AacSbrEnvelopeParser.ReadEnvelope(ref bits, left, Tables, header, false);
                    AacSbrEnvelopeParser.ReadNoise(ref bits, left, Tables, false);
                    AacSbrEnvelopeParser.ReadEnvelope(ref bits, right, Tables, header, true);
                    AacSbrEnvelopeParser.ReadNoise(ref bits, right, Tables, true);
                    AacSbrEnvelopeParser.UnmapCoupledChannels(left, right, Tables);
                }
                else
                {
                    right.ReadInverseFiltering(ref bits, Tables.NoiseCount);
                    AacSbrEnvelopeParser.ReadEnvelope(ref bits, left, Tables, header, false);
                    AacSbrEnvelopeParser.ReadEnvelope(ref bits, right, Tables, header, false);
                    AacSbrEnvelopeParser.ReadNoise(ref bits, left, Tables, false);
                    AacSbrEnvelopeParser.ReadNoise(ref bits, right, Tables, false);
                }
            }
            foreach (AacSbrChannelData channel in Channels)
            {
                channel.ReadHarmonics(ref bits, Tables.HighCount);
            }
            if (bits.Read(1) != 0)
            {
                int bytes = bits.Read(4);
                if (bytes == 15)
                {
                    bytes += bits.Read(8);
                }
                AacBitReader extension = bits.ReadSubstream(bytes * 8);
                while (extension.Remaining > 7)
                {
                    int extensionId = extension.Read(2);
                    if (extensionId == 2)
                    {
                        if (Channels.Length != 1 || HasParametricStereoData)
                        {
                            throw new InvalidDataException("A PS payload requires one monaural SBR element and cannot be repeated.");
                        }
                        ParametricStereo ??= new AacPsData();
                        ParametricStereo.Read(ref extension);
                        HasParametricStereoData = true;
                    }
                    else
                    {
                        extension.Skip(extension.Remaining);
                    }
                }
            }
            if (bits.Remaining > 7)
            {
                throw new InvalidDataException("The SBR payload has unexpected trailing data.");
            }
            bits.Skip(bits.Remaining);
        }

        private static void VerifyCrc(ref AacBitReader bits)
        {
            int expected = bits.Read(10);
            AacBitReader data = bits;
            int crc = 0;
            while (data.Remaining != 0)
            {
                int bit = data.Read(1) ^ ((crc >> 9) & 1);
                crc = ((crc << 1) ^ (bit == 0 ? 0 : 0x233)) & 0x3ff;
            }
            if (crc != expected)
            {
                throw new InvalidDataException("The SBR checksum is invalid.");
            }
        }
    }
}
