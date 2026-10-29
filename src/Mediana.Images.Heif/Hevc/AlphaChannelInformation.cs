// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class AlphaChannelInformation(int bitDepth, int transparent, int opaque, bool increment, bool clip, bool clipToRange)
    {
        public int BitDepth { get; } = bitDepth;

        public byte Convert(int sample)
        {
            int minimum = Math.Min(transparent, opaque);
            int maximum = Math.Max(transparent, opaque);
            if (increment && (sample > minimum))
            {
                sample++;
            }
            if (clip)
            {
                if (clipToRange)
                {
                    sample = Math.Min(maximum, Math.Max(minimum, sample));
                }
                else
                {
                    sample = (sample * 2) > (transparent + opaque) ? maximum : minimum;
                }
            }
            int numerator = opaque > transparent ? sample - transparent : transparent - sample;
            int range = maximum - minimum;
            return (byte)Math.Min(255, Math.Max(0, ((numerator * 255) + (range / 2)) / range));
        }

        public static AlphaChannelInformation? Read(ArraySegment<byte> payload)
        {
            BitReader header = new BitReader(payload);
            if (header.ReadFlag())
            {
                HevcSyntax.TrailingBits(header);
                return null;
            }
            int use = header.Read(3);
            int depth = header.Read(3) + 8;
            if (use > 2)
            {
                return null;
            }
            AlphaChannelInformation? information = ReadValues(payload, depth, depth + 1);
            if (information is null && (depth == 8))
            {
                information = ReadValues(payload, depth, depth);
            }
            return information ?? throw new InvalidDataException("Invalid alpha channel information.");
        }

        private static AlphaChannelInformation? ReadValues(ArraySegment<byte> payload, int depth, int valueBits)
        {
            BitReader reader = new BitReader(payload);
            reader.SkipBits(7);
            if (reader.BitsRemaining < ((valueBits * 2) + 3))
            {
                return null;
            }
            int transparent = reader.Read(valueBits);
            int opaque = reader.Read(valueBits);
            bool increment = reader.ReadFlag();
            bool clip = reader.ReadFlag();
            bool clipToRange = clip && reader.ReadFlag();
            int range = Math.Abs(opaque - transparent);
            if ((range == 0) || (increment && ((range & (range - 1)) != 0)) ||
                (reader.BitsRemaining == 0) || (reader.BitsRemaining > 7) || !reader.ReadFlag())
            {
                return null;
            }
            while (reader.BitsRemaining > 0)
            {
                if (reader.ReadFlag())
                {
                    return null;
                }
            }
            return new AlphaChannelInformation(depth, transparent, opaque, increment, clip, clipToRange);
        }
    }
}
