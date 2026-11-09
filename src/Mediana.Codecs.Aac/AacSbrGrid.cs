// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacSbrGrid
    {
        public const int MaximumEnvelopes = 5;
        private const int TimeSlots = 16;
        private readonly int[] borders = new int[MaximumEnvelopes + 1];
        private readonly bool[] highResolution = new bool[MaximumEnvelopes];
        private readonly int[] noiseBorders = new int[3];

        public int FrameClass { get; private set; }
        public int EnvelopeCount { get; private set; }
        public int NoiseEnvelopeCount => EnvelopeCount == 1 ? 1 : 2;
        public int AttackEnvelope { get; private set; }
        public ReadOnlySpan<int> Borders => borders.AsSpan(0, EnvelopeCount + 1);
        public ReadOnlySpan<bool> HighResolution => highResolution.AsSpan(0, EnvelopeCount);
        public ReadOnlySpan<int> NoiseBorders => noiseBorders.AsSpan(0, NoiseEnvelopeCount + 1);

        public void Read(ref AacBitReader bits)
        {
            FrameClass = bits.Read(2);
            AttackEnvelope = -1;
            int pointer = 0;
            switch (FrameClass)
            {
                case 0:
                    EnvelopeCount = 1 << bits.Read(2);
                    RequireCount(EnvelopeCount);
                    highResolution.AsSpan(0, EnvelopeCount).Fill(bits.Read(1) != 0);
                    for (int index = 0; index <= EnvelopeCount; index++)
                    {
                        borders[index] = index * TimeSlots / EnvelopeCount;
                    }
                    break;
                case 1:
                    int trailing = TimeSlots + bits.Read(2);
                    int backwards = bits.Read(2);
                    EnvelopeCount = backwards + 1;
                    borders[0] = 0;
                    borders[EnvelopeCount] = trailing;
                    for (int index = EnvelopeCount - 1; index > 0; index--)
                    {
                        trailing -= 2 + 2 * bits.Read(2);
                        borders[index] = trailing;
                    }
                    pointer = ReadPointer(ref bits);
                    AttackEnvelope = pointer == 0 ? -1 : EnvelopeCount + 1 - pointer;
                    for (int index = EnvelopeCount - 1; index >= 0; index--)
                    {
                        highResolution[index] = bits.Read(1) != 0;
                    }
                    break;
                case 2:
                    int leading = bits.Read(2);
                    int forwards = bits.Read(2);
                    EnvelopeCount = forwards + 1;
                    borders[0] = leading;
                    for (int index = 1; index < EnvelopeCount; index++)
                    {
                        leading += 2 + 2 * bits.Read(2);
                        borders[index] = leading;
                    }
                    borders[EnvelopeCount] = TimeSlots;
                    pointer = ReadPointer(ref bits);
                    AttackEnvelope = pointer <= 1 ? -1 : pointer - 1;
                    ReadResolutions(ref bits);
                    break;
                case 3:
                    int start = bits.Read(2);
                    int end = TimeSlots + bits.Read(2);
                    int left = bits.Read(2);
                    int right = bits.Read(2);
                    EnvelopeCount = left + right + 1;
                    RequireCount(EnvelopeCount);
                    borders[0] = start;
                    borders[EnvelopeCount] = end;
                    for (int index = 1; index <= left; index++)
                    {
                        start += 2 + 2 * bits.Read(2);
                        borders[index] = start;
                    }
                    for (int index = EnvelopeCount - 1; index > left; index--)
                    {
                        end -= 2 + 2 * bits.Read(2);
                        borders[index] = end;
                    }
                    pointer = ReadPointer(ref bits);
                    AttackEnvelope = pointer == 0 ? -1 : EnvelopeCount + 1 - pointer;
                    ReadResolutions(ref bits);
                    break;
                default:
                    throw new InvalidDataException("The SBR frame class is invalid.");
            }
            Validate();
            noiseBorders[0] = borders[0];
            noiseBorders[NoiseEnvelopeCount] = borders[EnvelopeCount];
            if (EnvelopeCount > 1)
            {
                int middle = FrameClass switch
                {
                    0 => EnvelopeCount / 2,
                    1 or 3 => pointer <= 1 ? EnvelopeCount - 1 : EnvelopeCount + 1 - pointer,
                    2 => pointer switch
                    {
                        0 => 1,
                        1 => EnvelopeCount - 1,
                        _ => pointer - 1
                    },
                    _ => throw new InvalidDataException("The SBR frame class is invalid.")
                };
                if (middle <= 0 || middle >= EnvelopeCount)
                {
                    throw new InvalidDataException("The SBR noise envelope border is invalid.");
                }
                noiseBorders[1] = borders[middle];
            }
        }

        public void CopyFrom(AacSbrGrid source)
        {
            FrameClass = source.FrameClass;
            EnvelopeCount = source.EnvelopeCount;
            AttackEnvelope = source.AttackEnvelope;
            source.borders.CopyTo(borders, 0);
            source.highResolution.CopyTo(highResolution, 0);
            source.noiseBorders.CopyTo(noiseBorders, 0);
        }

        private int ReadPointer(ref AacBitReader bits)
        {
            int width = 0;
            while ((1 << width) < EnvelopeCount + 1)
            {
                width++;
            }
            int pointer = bits.Read(width);
            if (pointer > EnvelopeCount)
            {
                throw new InvalidDataException("The SBR envelope pointer is invalid.");
            }
            return pointer;
        }

        private void ReadResolutions(ref AacBitReader bits)
        {
            for (int index = 0; index < EnvelopeCount; index++)
            {
                highResolution[index] = bits.Read(1) != 0;
            }
        }

        private void Validate()
        {
            if (borders[0] > 3 || borders[EnvelopeCount] < TimeSlots || borders[EnvelopeCount] > TimeSlots + 3)
            {
                throw new InvalidDataException("The SBR envelope lies outside its frame.");
            }
            for (int index = 0; index < EnvelopeCount; index++)
            {
                if (borders[index] >= borders[index + 1])
                {
                    throw new InvalidDataException("The SBR envelope time borders overlap.");
                }
            }
        }

        private static void RequireCount(int count)
        {
            if (count is < 1 or > MaximumEnvelopes)
            {
                throw new InvalidDataException("The SBR envelope count is invalid.");
            }
        }
    }
}
