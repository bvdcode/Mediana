// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264CabacCheckpointTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(4_000_000);
            long checks = 0;
            double maximumError = 0;
            for (int seed = 0; seed < 512; seed++)
            {
                byte[] control = Encode(seed, false);
                byte[] restored = Encode(seed, true);
                if (!control.AsSpan().SequenceEqual(restored)) { throw new InvalidDataException($"Checkpoint output differs: {seed}"); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Allocator did not return to zero"); }
            if (checks < 1_000_000) { throw new InvalidDataException("Incomplete CABAC rate/checkpoint verification."); }

            byte[] Encode(int seed, bool rollback)
            {
                using H264BitWriter bits = new(allocator, 1_000_000, 0x41, lengthPrefixed: true);
                H264CabacEncoder engine = new(bits);
                H264CabacContext[] contexts = new H264CabacContext[32];
                for (int i = 0; i < contexts.Length; i++) { contexts[i] = new((byte)((seed + i) % 64), ((seed + i) & 1) != 0); }
                Apply(new Random(seed * 13 + 1), seed == 0 ? 0 : 256, seed % 16);
                if (rollback)
                {
                    H264CabacContext[] saved = contexts.ToArray();
                    H264CabacEncoderCheckpoint first = engine.CreateCheckpoint();
                    double position = Position(engine); long count = engine.BinCount;
                    Apply(new Random(seed * 19 + 3), 512, seed % 16);
                    engine.Restore(first); saved.CopyTo(contexts, 0);
                    if (position != Position(engine) || count != engine.BinCount) { throw new InvalidDataException("Checkpoint position mismatch"); }
                    H264CabacEncoderCheckpoint second = engine.CreateCheckpoint();
                    Apply(new Random(seed * 43 + 5), 259, (seed + 1) % 16);
                    engine.Restore(second); saved.CopyTo(contexts, 0);
                    if (position != Position(engine) || count != engine.BinCount) { throw new InvalidDataException("Second checkpoint mismatch"); }
                }
                Apply(new Random(seed * 19 + 3), 512, seed % 16);
                engine.WriteTermination(true);
                using MediaBuffer data = bits.TryFinishCabac(engine.BinCount, 1, 384 * 8) ?? throw new InvalidDataException("Budget overflow");
                return data.Memory.ToArray();

                void Apply(Random random, int count, int kind)
                {
                    int dryRange = engine.CurrentRange, initialRange = dryRange, dryBits = 0;
                    double initialPosition = Position(engine);
                    H264CabacCostWriter counter = new();
                    counter.Reset(engine.CurrentRange);
                    H264CabacContext[] dryContexts = contexts.ToArray();
                    for (int n = 0; n < count; n++)
                    {
                        double before = Position(engine);
                        int range = engine.CurrentRange;
                        double expected;
                        int operation = random.Next(17);
                        bool value = kind switch { 0 => false, 1 => true, _ => random.Next(2) != 0 };
                        if (operation == 0)
                        {
                            dryRange -= 2;
                            NormalizeDry();
                            counter.WriteTermination(false);
                            engine.WriteTermination(false);
                            expected = Math.Log2(range) - Math.Log2(range - 2);
                        }
                        else if (operation < 5)
                        {
                            dryBits++;
                            counter.WriteBypass(value);
                            engine.WriteBypass(value);
                            expected = 1;
                        }
                        else
                        {
                            int index = random.Next(contexts.Length);
                            H264CabacContext context = contexts[index];
                            int lps = H264CabacTables.LpsRange(context.State, range);
                            int selected = value == context.MostProbableSymbol ? range - lps : lps;
                            expected = Math.Log2(range) - Math.Log2(selected);
                            int dryLps = H264CabacTables.LpsRange(context.State, dryRange);
                            dryRange = value == context.MostProbableSymbol ? dryRange - dryLps : dryLps;
                            NormalizeDry();
                            counter.WriteBin(value, ref dryContexts[index]);
                            engine.WriteBin(value, ref contexts[index]);
                        }
                        double actual = Position(engine) - before;
                        double difference = Math.Abs(actual - expected);
                        if (difference > maximumError) { maximumError = difference; }
                        if (difference > 1e-10 || actual < -1e-10) { throw new InvalidDataException($"Position invariant {seed}/{n}: {actual:R} != {expected:R}"); }
                        double dryCost = dryBits + Math.Log2(initialRange) - Math.Log2(dryRange);
                        if (counter.Range != dryRange || Math.Abs(counter.Cost - dryCost) > 1e-10 ||
                            !dryContexts.AsSpan().SequenceEqual(contexts))
                        { throw new InvalidDataException("CABAC cost writer diverged from its normative range/context model."); }
                        if (engine.CurrentRange != dryRange || Math.Abs((Position(engine) - initialPosition) - dryCost) > 1e-10)
                        { throw new InvalidDataException("Dry range/count model differs from real encoder position"); }
                        checks++;
                    }
                    void NormalizeDry()
                    {
                        int shift = System.Numerics.BitOperations.LeadingZeroCount((uint)dryRange) - 23;
                        int initial = dryRange, referenceShift = 0;
                        while (initial < 256) { initial <<= 1; referenceShift++; }
                        if (referenceShift != shift) { throw new InvalidDataException("Dry renormalization shift mismatch"); }
                        dryRange <<= shift; dryBits += shift;
                    }
                }
            }

            static double Position(H264CabacEncoder engine)
            {
                H264CabacRateSample sample = engine.RateSample;
                return sample.Bits - Math.Log2(sample.Range);
            }
        }
    }
}
