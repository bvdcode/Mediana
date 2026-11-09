// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>dec3 payload, including every independent substream, specified by ETSI TS 102 366 Annex F.6.</summary>
    public class Eac3SpecificConfiguration
    {
        private Eac3SpecificConfiguration(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2) { throw new InvalidDataException("The E-AC-3 specific configuration is truncated."); }
            int prefix = BinaryPrimitives.ReadUInt16BigEndian(data);
            DataRateKilobitsPerSecond = prefix >> 3;
            Eac3SubstreamConfiguration[] streams = new Eac3SubstreamConfiguration[(prefix & 7) + 1];
            int offset = 2;
            for (int index = 0; index < streams.Length; index++)
            {
                if (data.Length - offset < 3) { throw new InvalidDataException("An E-AC-3 substream configuration is truncated."); }
                int sampleRate = Ac3ChannelLayout.GetSampleRate(data[offset] >> 6);
                int bsid = data[offset] >> 1 & 31, mode = data[offset + 1] >> 1 & 7;
                int dependent = data[offset + 2] >> 1 & 15, locations = 0;
                if ((data[offset] & 1) != 0 || (data[offset + 2] & 0xe0) != 0 || bsid is < 11 or > 16)
                { throw new InvalidDataException("The E-AC-3 specific configuration contains reserved fields."); }
                if (dependent != 0)
                {
                    if (data.Length - offset < 4) { throw new InvalidDataException("The E-AC-3 dependent channel locations are truncated."); }
                    locations = (data[offset + 2] & 1) << 8 | data[offset + 3];
                }
                else if ((data[offset + 2] & 1) != 0)
                { throw new InvalidDataException("The E-AC-3 specific configuration contains a reserved padding bit."); }
                streams[index] = new(sampleRate, bsid, data[offset + 1] >> 4 & 7, mode,
                    (data[offset + 1] & 1) != 0, (data[offset + 1] & 128) != 0, dependent, locations);
                offset += dependent == 0 ? 3 : 4;
            }
            Substreams = Array.AsReadOnly(streams);
        }

        public int DataRateKilobitsPerSecond { get; }
        public IReadOnlyList<Eac3SubstreamConfiguration> Substreams { get; }
        public static Eac3SpecificConfiguration Read(ReadOnlySpan<byte> data) => new(data);
    }
}
