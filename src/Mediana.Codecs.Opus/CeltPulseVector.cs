// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Opus
{
    /// <summary>Pulse integers and normalized coefficients share their 32-bit workspace.</summary>
    internal static class CeltPulseVector
    {
        public static uint Read(CeltPulseCodebook codebook, ref OpusRangeDecoder decoder, scoped Span<float> vector,
            int pulses, int blocks, CeltSpreading spreading, float gain)
        {
            ArgumentNullException.ThrowIfNull(codebook);
            ArgumentOutOfRangeException.ThrowIfLessThan(vector.Length, 2, nameof(vector));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pulses);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blocks);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(blocks, CeltBandTables.MaximumTimeBlocks);
            if (!BitOperations.IsPow2(blocks) || vector.Length % blocks != 0)
            {
                throw new ArgumentException("CELT pulse blocks require a power-of-two count dividing the vector.", nameof(blocks));
            }
            if (!float.IsFinite(gain) || gain is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(gain)); }
            _ = CeltSpreadingRotation.GetFactor(spreading);
            Span<int> integers = MemoryMarshal.Cast<float, int>(vector);
            ulong energy = codebook.Decode(ref decoder, integers, pulses);
            uint collapse = 0;
            int width = vector.Length / blocks;
            for (int block = 0; block < blocks; block++)
            {
                int occupied = 0;
                foreach (int coefficient in integers.Slice(block * width, width)) { occupied |= coefficient; }
                if (occupied != 0) { collapse |= 1u << block; }
            }
            float scale = (1f / MathF.Sqrt(energy)) * gain;
            int at = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<float> multiplier = new(scale);
                for (; at <= vector.Length - Vector<int>.Count; at += Vector<int>.Count)
                {
                    Vector<int> coefficients = new(integers[at..]);
                    (Vector.ConvertToSingle(coefficients) * multiplier).CopyTo(vector[at..]);
                }
            }
            for (; at < vector.Length; at++) { vector[at] = integers[at] * scale; }
            CeltSpreadingRotation.ApplyInverse(vector, pulses, blocks, spreading);
            return collapse;
        }
    }
}
