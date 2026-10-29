// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal static class HevcSyntax
    {
        public static int Ue(BitReader reader, int maximum, string field)
        {
            uint value = reader.ReadUev();
            if (value > (uint)maximum)
            {
                throw new InvalidDataException("HEVC field is out of range: " + field + ".");
            }
            return (int)value;
        }

        public static int Se(BitReader reader, int minimum, int maximum, string field)
        {
            int value = reader.ReadSev();
            if ((value < minimum) || (value > maximum))
            {
                throw new InvalidDataException("HEVC field is out of range: " + field + ".");
            }
            return value;
        }

        public static BitReader Open(NalPayload nal, int type)
        {
            if (nal.Type != type)
            {
                throw new InvalidDataException("Unexpected HEVC parameter set type.");
            }
            BitReader reader = new BitReader(nal.Bytes);
            reader.SkipBits(16);
            return reader;
        }

        public static void Alignment(BitReader reader)
        {
            if (!reader.ReadFlag())
            {
                throw new InvalidDataException("Missing HEVC alignment stop bit.");
            }
            while (!reader.ByteAligned())
            {
                if (reader.ReadFlag())
                {
                    throw new InvalidDataException("Nonzero HEVC alignment padding.");
                }
            }
        }

        public static void TrailingBits(BitReader reader)
        {
            Alignment(reader);
            if (reader.BitsRemaining != 0)
            {
                throw new InvalidDataException("Unexpected data after HEVC parameter set.");
            }
        }

        public static int ProfileTierLevel(BitReader reader, int maxSubLayer)
        {
            reader.SkipBits(3);
            int profile = reader.Read(5);
            reader.SkipBits(32 + 48 + 8);
            if ((profile < 1) || (profile > 4))
            {
                throw new UnsupportedHeicFeatureException("HEVC profile " + profile);
            }
            int[] flags = new int[maxSubLayer];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = reader.Read(2);
            }
            if (maxSubLayer > 0)
            {
                reader.SkipBits((8 - maxSubLayer) * 2);
            }
            foreach (int flag in flags)
            {
                if ((flag & 2) != 0)
                {
                    reader.SkipBits(88);
                }
                if ((flag & 1) != 0)
                {
                    reader.SkipBits(8);
                }
            }

            return profile;
        }

        public static void SequenceExtensions(BitReader reader)
        {
            if (!reader.ReadFlag())
            {
                return;
            }
            bool range = reader.ReadFlag();
            if (reader.Read(7) != 0)
            {
                throw new UnsupportedHeicFeatureException("HEVC sequence extensions");
            }
            if (range && (reader.Read(9) != 0))
            {
                throw new UnsupportedHeicFeatureException("HEVC range extension tools");
            }
        }
    }
}
