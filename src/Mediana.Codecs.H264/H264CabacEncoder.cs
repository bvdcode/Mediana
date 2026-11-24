// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    /// <summary>Encodes arithmetic bins under H.264 clause 9.3.4. Borrows the unfinished NAL writer.</summary>
    internal class H264CabacEncoder
    {
        private const int WordBits = sizeof(uint) * 8;
        private const int IntervalBits = 9;
        private readonly H264BitWriter bits;
        private int range = 510;
        private ulong low;
        private int pendingBits;
        private uint heldWord;
        private bool hasHeldWord;
        private long outstandingWords;
        private bool terminated;

        public H264CabacEncoder(H264BitWriter bits)
        {
            ArgumentNullException.ThrowIfNull(bits);
            if ((bits.BitPosition & 7) != 0)
            {
                throw new InvalidOperationException("H.264 CABAC must start on a byte boundary.");
            }
            this.bits = bits;
        }

        public long BinCount { get; private set; }

        public int CurrentRange => range;
        public H264CabacRateSample RateSample => new(
            terminated ? checked(bits.BitPosition + 1) :
                checked(bits.BitPosition + pendingBits + (outstandingWords + (hasHeldWord ? 1 : 0)) * WordBits), range);

        public H264CabacEncoderCheckpoint CreateCheckpoint() => new(bits.CreateCheckpoint(), range, low,
            pendingBits, heldWord, hasHeldWord, outstandingWords, terminated, BinCount);

        public void Restore(in H264CabacEncoderCheckpoint checkpoint)
        {
            bits.Restore(checkpoint.Writer);
            range = checkpoint.Range;
            low = checkpoint.Low;
            pendingBits = checkpoint.PendingBits;
            heldWord = checkpoint.HeldWord;
            hasHeldWord = checkpoint.HasHeldWord;
            outstandingWords = checkpoint.OutstandingWords;
            terminated = checkpoint.Terminated;
            BinCount = checkpoint.BinCount;
        }

        public void WriteBin(bool value, ref H264CabacContext context)
        {
            EnsureActive();
            int lps = H264CabacTables.LpsRange(context.State, range);
            range -= lps;
            if (value != context.MostProbableSymbol)
            {
                low += (uint)range;
                range = lps;
                context = context.NextLeastProbable();
            }
            else
            {
                context = context.NextMostProbable();
            }
            if (range < 256)
            {
                Normalize();
            }
            BinCount = checked(BinCount + 1);
        }

        public void WriteBypass(bool value)
        {
            EnsureActive();
            low = (low << 1) + (value ? (uint)range : 0);
            Advance(1);
            BinCount = checked(BinCount + 1);
        }

        public void WriteBypassBits(uint value, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            if (count < 32 && value >> count != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            EnsureActive();
            BinCount = checked(BinCount + count);
            while (count > 0)
            {
                // A partial word, the coding interval and 16 more bins fit in the 64-bit accumulator.
                int chunk = Math.Min(count, 16);
                count -= chunk;
                uint bins = (value >> count) & ((1u << chunk) - 1);
                low = (low << chunk) + (ulong)(uint)range * bins;
                Advance(chunk);
            }
        }

        /// <summary>A true bin flushes the arithmetic interval, supplies the stop bit and aligns the writer.
        /// For I_PCM the same flush precedes PCM samples; start a new engine afterwards, retaining context states.</summary>
        public void WriteTermination(bool value)
        {
            EnsureActive();
            range -= 2;
            if (value)
            {
                low += (uint)range;
                range = 2;
            }
            if (range < 256)
            {
                Normalize();
            }
            if (value)
            {
                int suffixBits = pendingBits + 1;
                EmitHeld((uint)(low >> (suffixBits + 8)));
                ulong suffix = (low >> 8) & ((1UL << suffixBits) - 1);
                bits.WriteBits((suffix << 1) | 1, suffixBits + 1);
                int alignment = (int)((8 - (bits.BitPosition & 7)) & 7);
                bits.WriteBits(0, alignment);
                terminated = true;
            }
            BinCount = checked(BinCount + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Normalize()
        {
            int shift = IntervalBits - 1 - BitOperations.Log2((uint)range);
            range <<= shift;
            low <<= shift;
            Advance(shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance(int count)
        {
            pendingBits += count;
            if (pendingBits >= WordBits)
            {
                FlushWord();
            }
        }

        private void FlushWord()
        {
            pendingBits -= WordBits;
            // Keep the preceding word until a following digit settles its carry, even across a run of all ones.
            int shift = pendingBits + IntervalBits;
            ulong digit = low >> shift;
            low &= (1UL << shift) - 1;
            if (hasHeldWord && digit == uint.MaxValue)
            {
                outstandingWords = checked(outstandingWords + 1);
                return;
            }
            EmitHeld((uint)(digit >> WordBits));
            heldWord = (uint)digit;
            hasHeldWord = true;
        }

        private void EmitHeld(uint carry)
        {
            if (!hasHeldWord)
            {
                return;
            }
            bits.WriteBits(heldWord + carry, WordBits);
            while (outstandingWords > 0)
            {
                bits.WriteBits(carry == 0 ? uint.MaxValue : 0, WordBits);
                outstandingWords--;
            }
        }

        private void EnsureActive()
        {
            if (terminated)
            {
                throw new InvalidOperationException("The H.264 CABAC encoding process has terminated.");
            }
        }
    }
}
