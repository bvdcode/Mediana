// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal class DecodedPicture : IDisposable
    {
        public DecodedPicture(SequenceParameters sequence)
        {
            Sequence = sequence;
            Luma = new SamplePlane(sequence.Width, sequence.Height, sequence.BitDepth);
            bool initialized = false;
            try
            {
                if (sequence.ChromaFormat != 0)
                {
                    Cb = new SamplePlane(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC, sequence.BitDepth);
                    Cr = new SamplePlane(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC, sequence.BitDepth);
                }

                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    Dispose();
                }
            }
        }

        public SequenceParameters Sequence { get; }
        public SamplePlane Luma { get; }
        public SamplePlane? Cb { get; }
        public SamplePlane? Cr { get; }

        public void Dispose()
        {
            Luma.Dispose();
            Cb?.Dispose();
            Cr?.Dispose();
        }
    }
}
