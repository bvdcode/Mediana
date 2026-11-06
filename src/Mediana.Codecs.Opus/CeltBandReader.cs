// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class CeltBandReader
    {
        public static uint Read(ref OpusRangeDecoder decoder, ref CeltBandDecoderState state, scoped Span<float> vector,
            int budgetEighths, CeltBandResolution resolution, scoped ReadOnlySpan<float> lowBand,
            scoped Span<float> lowBandOutput, float gain, uint fill)
        {
            Validate(state, vector, resolution, lowBand, lowBandOutput, gain);
            if (vector.Length == 1)
            {
                ReadSign(ref decoder, ref state, vector);
                if (!lowBandOutput.IsEmpty) { lowBandOutput[0] = vector[0]; }
                return 1;
            }
            int blocks = resolution.Blocks, width = vector.Length / blocks;
            int recombine = Math.Max(0, resolution.TimeFrequency);
            int change = resolution.TimeFrequency;
            int divided = 0;
            Span<float> foldingCopy = stackalloc float[CeltBandTables.MaximumBandDimensions];
            scoped ReadOnlySpan<float> folding = lowBand;
            if (!lowBand.IsEmpty && (recombine > 0 || ((width & 1) == 0 && change < 0) || blocks > 1))
            {
                lowBand[..vector.Length].CopyTo(foldingCopy);
                Span<float> mutable = foldingCopy[..vector.Length];
                for (int at = 0; at < recombine; at++)
                {
                    CeltSpectralOrder.Haar(mutable, vector.Length >> at, 1 << at);
                }
                int foldBlocks = blocks >> recombine, foldWidth = width << recombine;
                int foldChange = change;
                while ((foldWidth & 1) == 0 && foldChange < 0)
                {
                    CeltSpectralOrder.Haar(mutable, foldWidth, foldBlocks);
                    foldBlocks *= 2;
                    foldWidth /= 2;
                    foldChange++;
                }
                if (foldBlocks > 1)
                {
                    CeltSpectralOrder.Deinterleave(mutable, foldWidth >> recombine, foldBlocks << recombine, resolution.Blocks == 1);
                }
                folding = mutable;
            }
            for (int at = 0; at < recombine; at++)
            {
                fill = InterleavedMask[(int)(fill & 15)] | ((uint)InterleavedMask[(int)(fill >> 4)] << 2);
            }
            blocks >>= recombine;
            width <<= recombine;
            while ((width & 1) == 0 && change < 0)
            {
                fill |= fill << blocks;
                blocks *= 2;
                width /= 2;
                divided++;
                change++;
            }
            uint mask = CeltPartitionReader.Read(ref decoder, ref state, vector, budgetEighths, blocks, folding, resolution.FrameShift, gain, fill);
            if (blocks > 1)
            {
                CeltSpectralOrder.Interleave(vector, width >> recombine, blocks << recombine, resolution.Blocks == 1);
            }
            for (int at = 0; at < divided; at++)
            {
                blocks /= 2;
                width *= 2;
                mask |= mask >> blocks;
                CeltSpectralOrder.Haar(vector, width, blocks);
            }
            for (int at = 0; at < recombine; at++)
            {
                mask = DeinterleavedMask[(int)mask];
                CeltSpectralOrder.Haar(vector, vector.Length >> at, 1 << at);
            }
            blocks <<= recombine;
            if (!lowBandOutput.IsEmpty)
            {
                float scale = MathF.Sqrt(vector.Length);
                for (int at = 0; at < vector.Length; at++) { lowBandOutput[at] = scale * vector[at]; }
            }
            return mask & ((1u << blocks) - 1);
        }

        public static void ReadSign(ref OpusRangeDecoder decoder, ref CeltBandDecoderState state, scoped Span<float> vector)
        {
            bool negative = false;
            if (state.RemainingEighths >= 8)
            {
                negative = decoder.ReadRawBits(1) != 0;
                state.RemainingEighths -= 8;
            }
            if (negative) { vector[0] = -1; }
            else { vector[0] = 1; }
        }

        private static void Validate(CeltBandDecoderState state, Span<float> vector, CeltBandResolution resolution,
            ReadOnlySpan<float> lowBand, Span<float> lowBandOutput, float gain)
        {
            ArgumentNullException.ThrowIfNull(state.Codebook);
            ArgumentOutOfRangeException.ThrowIfNegative(state.Band);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state.Band, CeltBandTables.BandCount);
            ArgumentOutOfRangeException.ThrowIfNegative(resolution.FrameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(resolution.FrameShift, 3);
            int expected = CeltBandTables.Width(state.Band) << resolution.FrameShift;
            if (vector.Length != expected) { throw new ArgumentException("The CELT vector dimensions do not match its band.", nameof(vector)); }
            if (resolution.Blocks < 1 || resolution.Blocks > 1 << resolution.FrameShift ||
                !BitOperations.IsPow2(resolution.Blocks) || vector.Length % resolution.Blocks != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution));
            }
            if (resolution.TimeFrequency < -3 || resolution.TimeFrequency > BitOperations.Log2((uint)resolution.Blocks))
            {
                throw new ArgumentOutOfRangeException(nameof(resolution));
            }
            int blocks = resolution.Blocks, width = vector.Length / blocks;
            for (int change = resolution.TimeFrequency; change < 0 && (width & 1) == 0; change++)
            {
                width /= 2;
                blocks *= 2;
            }
            if (blocks > CeltBandTables.MaximumTimeBlocks) { throw new ArgumentOutOfRangeException(nameof(resolution)); }
            if ((!lowBand.IsEmpty && lowBand.Length < expected) || (!lowBandOutput.IsEmpty && lowBandOutput.Length < expected))
            {
                throw new ArgumentException("CELT folding buffers must cover their complete band.");
            }
            if (!float.IsFinite(gain) || gain is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(gain)); }
            _ = CeltSpreadingRotation.GetFactor(state.Spreading);
        }

        private static ReadOnlySpan<byte> InterleavedMask => [0, 1, 1, 1, 2, 3, 3, 3, 2, 3, 3, 3, 2, 3, 3, 3];
        private static ReadOnlySpan<byte> DeinterleavedMask => [0, 3, 12, 15, 48, 51, 60, 63, 192, 195, 204, 207, 240, 243, 252, 255];
    }
}
