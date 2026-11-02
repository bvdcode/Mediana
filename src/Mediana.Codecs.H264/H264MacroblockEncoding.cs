// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264MacroblockEncoding
    {
        // Rec. ITU-T H.264, A.3.1(n), CAVLC macroblock_layer bit limit.
        private const int MaximumBits = 3200;

        public static bool Fits(H264BitWriter bits, in H264BitWriterCheckpoint checkpoint) =>
            bits.BitPosition - checkpoint.BitPosition <= MaximumBits;

        public static void WriteQuantizerDelta(H264BitWriter bits, int previous, int current)
        {
            int delta = current - previous;
            if (delta < -26)
            {
                delta += 52;
            }
            else if (delta > 25)
            {
                delta -= 52;
            }
            bits.WriteSigned(delta);
        }

        public static int IncreaseQuantizer(int quantizer) => quantizer < 51 ? quantizer + 1 :
            throw new InvalidOperationException("The H.264 macroblock cannot satisfy Baseline coding constraints at maximum quantization.");
    }
}
