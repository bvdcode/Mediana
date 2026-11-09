// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264CabacEncodingTestInput
    {
        public static void Verify()
        {
            for (int state = 0; state < 64; state++)
            {
                foreach (bool mps in new[] { false, true })
                {
                    for (int pattern = 0; pattern < 4; pattern++)
                    {
                        VerifySequence(state, mps, pattern);
                    }
                }
            }
        }

        private static void VerifySequence(int state, bool mps, int pattern)
        {
            MediaBufferAllocator allocator = new(4096);
            using (H264BitWriter bits = new(allocator, 4096, 0x41))
            {
                H264CabacEncoder encoder = new(bits);
                H264CabacIntervalWriter exact = new(26);
                H264CabacContext[] contexts = new H264CabacContext[4];
                for (int slot = 0; slot < contexts.Length; slot++)
                {
                    contexts[slot] = new((byte)((state + slot * 17) & 63), mps);
                    exact.Initialize(slot, (byte)(contexts[slot].State * 2 + (mps ? 1 : 0)));
                }
                H264CabacContext[] initial = contexts.ToArray();
                Random random = new(state * 81 + pattern * 31 + (mps ? 1 : 0));
                bool[] values = new bool[192];
                int[] slots = new int[values.Length];
                uint[] bypass = new uint[values.Length];
                int[] widths = [0, 1, 7, 16, 32];
                long binCount = 1;
                for (int index = 0; index < values.Length; index++)
                {
                    int slot = random.Next(contexts.Length);
                    bool value = pattern switch
                    {
                        0 => contexts[slot].MostProbableSymbol,
                        1 => !contexts[slot].MostProbableSymbol,
                        2 => (index & 1) != 0,
                        3 => random.Next(2) != 0,
                        _ => throw new InvalidOperationException("Invalid CABAC encoding pattern.")
                    };
                    slots[index] = slot;
                    values[index] = value;
                    encoder.WriteBin(value, ref contexts[slot]);
                    exact.Decision(value, slot, 0, 0);
                    int count = widths[index % widths.Length];
                    uint word = (uint)random.NextInt64(1L << count);
                    bypass[index] = word;
                    encoder.WriteBypassBits(word, count);
                    exact.Bits(word, count);
                    encoder.WriteTermination(false);
                    exact.Termination(false);
                    binCount += 2 + count;
                }
                encoder.WriteTermination(true);
                exact.Termination(true);
                using MediaBuffer packet = bits.TryFinishCompletedRbsp()
                    ?? throw new InvalidOperationException("The CABAC encoding fixture exceeded its packet budget.");
                if (encoder.BinCount != binCount || !exact.Contains(packet.Memory.Span[1..]))
                {
                    throw new InvalidDataException("The CABAC code point is outside the independent exact arithmetic interval.");
                }
                H264BitReader reader = new(packet.Memory.Span[1..]);
                H264CabacDecoder decoder = new(ref reader);
                for (int index = 0; index < values.Length; index++)
                {
                    if (decoder.ReadBin(ref reader, ref initial[slots[index]]) != values[index] ||
                        decoder.ReadBypassBits(ref reader, widths[index % widths.Length]) != bypass[index] ||
                        decoder.ReadTermination(ref reader))
                    {
                        throw new InvalidDataException("The CABAC encoder changed a decision, bypass or termination bin.");
                    }
                }
                if (!decoder.ReadTermination(ref reader))
                {
                    throw new InvalidDataException("The CABAC encoder failed to terminate.");
                }
                for (int slot = 0; slot < contexts.Length; slot++)
                {
                    if (contexts[slot].State != initial[slot].State || contexts[slot].MostProbableSymbol != initial[slot].MostProbableSymbol)
                    {
                        throw new InvalidDataException("The CABAC encoder changed final context state.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The CABAC encoding fixture retained packet storage.");
            }
        }
    }
}
