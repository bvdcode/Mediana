// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Formats.Audio
{
    /// <summary>Dependent E-AC-3 coded channel locations from ETSI TS 102 366 Table E.1.4.</summary>
    public static class Eac3ChannelMap
    {
        private const ushort PairMask = 0x0674;
        public static int GetChannelCount(ushort map) => BitOperations.PopCount(map) + BitOperations.PopCount((uint)(map & PairMask));

        public static string[] GetProgramChannelOrder(ReadOnlySpan<Eac3FrameMetadata> substreams)
        {
            List<string> channels = [];
            foreach (Eac3FrameMetadata substream in substreams)
            {
                foreach (string channel in substream.GetChannelOrder())
                {
                    if (channels.Contains(channel)) { continue; }
                    if (channels.Count == 16) { throw new InvalidDataException("An E-AC-3 programme exceeds sixteen rendered channels."); }
                    channels.Add(channel);
                }
            }
            return channels.ToArray();
        }

        public static int GetAdditionalLocations(ushort map)
        {
            int locations = 0;
            for (int bit = 0; bit < 8; bit++)
            { if ((map & 1 << (10 - bit)) != 0) { locations |= 1 << bit; } }
            if ((map & 2) != 0) { locations |= 256; }
            return locations;
        }

        public static string[] GetChannelOrder(ushort map)
        {
            List<string> channels = new(GetChannelCount(map));
            for (int bit = 0; bit < 16; bit++)
            {
                if ((map & 1 << (15 - bit)) == 0) { continue; }
                switch (bit)
                {
                    case 0: channels.Add("front-left"); break;
                    case 1: channels.Add("front-center"); break;
                    case 2: channels.Add("front-right"); break;
                    case 3: channels.Add("side-left"); break;
                    case 4: channels.Add("side-right"); break;
                    case 5: channels.Add("front-left-of-center"); channels.Add("front-right-of-center"); break;
                    case 6: channels.Add("back-left"); channels.Add("back-right"); break;
                    case 7: channels.Add("back-center"); break;
                    case 8: channels.Add("top-center"); break;
                    case 9: channels.Add("surround-direct-left"); channels.Add("surround-direct-right"); break;
                    case 10: channels.Add("wide-left"); channels.Add("wide-right"); break;
                    case 11: channels.Add("top-front-left"); channels.Add("top-front-right"); break;
                    case 12: channels.Add("top-front-center"); break;
                    case 13: channels.Add("top-side-left"); channels.Add("top-side-right"); break;
                    case 14: channels.Add("low-frequency-2"); break;
                    case 15: channels.Add("low-frequency"); break;
                    default: throw new InvalidOperationException();
                }
            }
            return channels.ToArray();
        }
    }
}
