// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Reads progressive macroblock binarizations under H.264 tables 9-34 through 9-41.</summary>
    internal static class H264CabacMacroblockSyntax
    {
        public static bool ReadSkip(ref H264BitReader bits, H264CabacReader reader, int neighbourContext)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 2);
            return reader.Read(ref bits, 11 + neighbourContext);
        }

        public static bool ReadBidirectionalSkip(ref H264BitReader bits, H264CabacReader reader, int neighbourContext)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 2);
            return reader.Read(ref bits, 24 + neighbourContext);
        }

        public static int ReadBidirectionalType(ref H264BitReader bits, H264CabacReader reader, int neighbourContext)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 2);
            if (!reader.Read(ref bits, 27 + neighbourContext))
            {
                return 0;
            }
            if (!reader.Read(ref bits, 30))
            {
                return reader.Read(ref bits, 32) ? 2 : 1;
            }
            int code = reader.Read(ref bits, 31) ? 8 : 0;
            for (int bit = 2; bit >= 0; bit--)
            {
                code |= (reader.Read(ref bits, 32) ? 1 : 0) << bit;
            }
            return code switch
            {
                < 8 => code + 3,
                >= 8 and <= 12 => 12 + (code - 8) * 2 + (reader.Read(ref bits, 32) ? 1 : 0),
                13 => 23 + ReadIntra(ref bits, reader, 32, true),
                14 => 11,
                15 => 22,
                _ => throw new InvalidDataException("Invalid H.264 CABAC B macroblock code.")
            };
        }

        public static int ReadBidirectionalSubType(ref H264BitReader bits, H264CabacReader reader)
        {
            if (!reader.Read(ref bits, 36))
            {
                return 0;
            }
            if (!reader.Read(ref bits, 37))
            {
                return reader.Read(ref bits, 39) ? 2 : 1;
            }
            if (!reader.Read(ref bits, 38))
            {
                return 3 + (reader.Read(ref bits, 39) ? 2 : 0) + (reader.Read(ref bits, 39) ? 1 : 0);
            }
            if (reader.Read(ref bits, 39))
            {
                return reader.Read(ref bits, 39) ? 12 : 11;
            }
            return 7 + (reader.Read(ref bits, 39) ? 2 : 0) + (reader.Read(ref bits, 39) ? 1 : 0);
        }

        public static int ReadIntraType(ref H264BitReader bits, H264CabacReader reader, int neighbourContext)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 2);
            return ReadIntra(ref bits, reader, 3 + neighbourContext, false);
        }

        public static int ReadPredictiveType(ref H264BitReader bits, H264CabacReader reader)
        {
            if (reader.Read(ref bits, 14))
            {
                return 5 + ReadIntra(ref bits, reader, 17, true);
            }
            if (reader.Read(ref bits, 15))
            {
                return reader.Read(ref bits, 17) ? 1 : 2;
            }
            return reader.Read(ref bits, 16) ? 3 : 0;
        }

        public static int ReadSubType(ref H264BitReader bits, H264CabacReader reader)
        {
            if (reader.Read(ref bits, 21))
            {
                return 0;
            }
            if (!reader.Read(ref bits, 22))
            {
                return 1;
            }
            return reader.Read(ref bits, 23) ? 2 : 3;
        }

        public static int ReadReference(ref H264BitReader bits, H264CabacReader reader, int neighbourContext, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 3);
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 31);
            int reference = 0;
            int context = 54 + neighbourContext;
            while (reader.Read(ref bits, context))
            {
                if (++reference > maximum)
                {
                    throw new InvalidDataException("The H.264 CABAC reference index exceeds the active reference list.");
                }
                context = reference == 1 ? 58 : 59;
            }
            return reference;
        }

        public static int ReadMotionDifference(ref H264BitReader bits, H264CabacReader reader, bool vertical, int absoluteNeighbourSum)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(absoluteNeighbourSum);
            int offset = vertical ? 47 : 40;
            int neighbour = absoluteNeighbourSum > 32 ? 2 : absoluteNeighbourSum > 2 ? 1 : 0;
            if (!reader.Read(ref bits, offset + neighbour))
            {
                return 0;
            }
            int magnitude = 1;
            while (magnitude < 9 && reader.Read(ref bits, offset + Math.Min(magnitude + 2, 6)))
            {
                magnitude++;
            }
            if (magnitude == 9)
            {
                int order = 3;
                while (reader.Bypass(ref bits))
                {
                    if (order >= 15)
                    {
                        throw new InvalidDataException("The H.264 CABAC motion difference exceeds the supported precision.");
                    }
                    magnitude += 1 << order++;
                }
                magnitude += (int)reader.Bits(ref bits, order);
            }
            int value = reader.Bypass(ref bits) ? -magnitude : magnitude;
            if (value is < short.MinValue or > short.MaxValue)
            {
                throw new InvalidDataException("The H.264 CABAC motion difference exceeds the supported precision.");
            }
            return value;
        }

        private static int ReadIntra(ref H264BitReader bits, H264CabacReader reader, int firstContext, bool predictive)
        {
            if (!reader.Read(ref bits, firstContext))
            {
                return 0;
            }
            if (reader.Termination(ref bits))
            {
                return 25;
            }
            int suffix = predictive ? firstContext + 1 : 6;
            int type = reader.Read(ref bits, suffix) ? 13 : 1;
            if (reader.Read(ref bits, suffix + 1))
            {
                type += reader.Read(ref bits, suffix + (predictive ? 1 : 2)) ? 8 : 4;
            }
            if (reader.Read(ref bits, suffix + (predictive ? 2 : 3)))
            {
                type += 2;
            }
            if (reader.Read(ref bits, suffix + (predictive ? 2 : 4)))
            {
                type++;
            }
            return type;
        }
    }
}
