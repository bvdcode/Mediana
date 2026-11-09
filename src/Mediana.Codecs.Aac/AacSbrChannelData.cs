// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacSbrChannelData
    {
        public AacSbrGrid Grid { get; } = new();
        public bool[] EnvelopeTimeDirection { get; } = new bool[5];
        public bool[] NoiseTimeDirection { get; } = new bool[2];
        public int[] InverseFiltering { get; } = new int[5];
        public int[] PreviousEnvelope { get; } = new int[64];
        public int[] PreviousNoise { get; } = new int[5];
        public int[] Envelope { get; } = new int[5 * 64];
        public int[] Noise { get; } = new int[2 * 5];
        public double[] Energy { get; } = new double[5 * 64];
        public double[] NoiseRatio { get; } = new double[2 * 5];
        public bool[] Harmonics { get; } = new bool[64];
        public bool CoarseAmplitude { get; set; }
        public bool HasPreviousEnvelope { get; set; }
        public bool HasPreviousNoise { get; set; }

        public void ReadDirections(ref AacBitReader bits)
        {
            for (int envelope = 0; envelope < Grid.EnvelopeCount; envelope++)
            {
                EnvelopeTimeDirection[envelope] = bits.Read(1) != 0;
            }
            for (int envelope = 0; envelope < Grid.NoiseEnvelopeCount; envelope++)
            {
                NoiseTimeDirection[envelope] = bits.Read(1) != 0;
            }
        }

        public void ReadInverseFiltering(ref AacBitReader bits, int count)
        {
            for (int index = 0; index < count; index++)
            {
                InverseFiltering[index] = bits.Read(2);
            }
        }

        public void ReadHarmonics(ref AacBitReader bits, int count)
        {
            Array.Clear(Harmonics);
            if (bits.Read(1) != 0)
            {
                for (int index = 0; index < count; index++)
                {
                    Harmonics[index] = bits.Read(1) != 0;
                }
            }
        }
    }
}
