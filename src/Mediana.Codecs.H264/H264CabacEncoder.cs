// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Encodes arithmetic bins under H.264 clause 9.3.4. Borrows the unfinished NAL writer.</summary>
    internal class H264CabacEncoder
    {
        private readonly H264BitWriter bits;
        private int range = 510;
        private int low;
        private long outstanding;
        private ulong outputBits;
        private int outputCount;
        private bool firstBit = true;
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

        public void WriteBin(bool value, ref H264CabacContext context)
        {
            EnsureActive();
            int lps = H264CabacTables.LpsRange(context.State, range);
            range -= lps;
            if (value != context.MostProbableSymbol)
            {
                low += range;
                range = lps;
                context = context.NextLeastProbable();
            }
            else
            {
                context = context.NextMostProbable();
            }
            Normalize();
            BinCount = checked(BinCount + 1);
        }

        public void WriteBypass(bool value)
        {
            EnsureActive();
            low = (low << 1) + (value ? range : 0);
            if (low >= 1024)
            {
                PutBit(true);
                low -= 1024;
            }
            else if (low < 512)
            {
                PutBit(false);
            }
            else
            {
                low -= 512;
                outstanding = checked(outstanding + 1);
            }
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
            for (int index = count - 1; index >= 0; index--)
            {
                WriteBypass((value & (1u << index)) != 0);
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
                low += range;
                range = 2;
            }
            Normalize();
            if (value)
            {
                PutBit((low & 512) != 0);
                if (outputCount != 0)
                {
                    bits.WriteBits(outputBits, outputCount);
                    outputBits = 0;
                    outputCount = 0;
                }
                bits.WriteBits((uint)(((low >> 7) & 3) | 1), 2);
                int alignment = (int)((8 - (bits.BitPosition & 7)) & 7);
                bits.WriteBits(0, alignment);
                terminated = true;
            }
            BinCount = checked(BinCount + 1);
        }

        private void Normalize()
        {
            while (range < 256)
            {
                if (low < 256)
                {
                    PutBit(false);
                }
                else if (low >= 512)
                {
                    PutBit(true);
                    low -= 512;
                }
                else
                {
                    low -= 256;
                    outstanding = checked(outstanding + 1);
                }
                range <<= 1;
                low <<= 1;
            }
        }

        private void PutBit(bool value)
        {
            if (firstBit)
            {
                firstBit = false;
            }
            else
            {
                outputBits = (outputBits << 1) | (value ? 1UL : 0);
                if (++outputCount == 64)
                {
                    bits.WriteBits(outputBits, 64);
                    outputBits = 0;
                    outputCount = 0;
                }
            }
            while (outstanding != 0)
            {
                int count = (int)Math.Min(outstanding, 64);
                ulong word = value ? 0 : ulong.MaxValue >> (64 - count);
                BufferBits(word, count);
                outstanding -= count;
            }
        }

        private void BufferBits(ulong word, int count)
        {
            int space = 64 - outputCount;
            if (count < space)
            {
                outputBits = (outputBits << count) | word;
                outputCount += count;
                return;
            }
            if (space == 64)
            {
                bits.WriteBits(word, 64);
            }
            else
            {
                bits.WriteBits((outputBits << space) | (word >> (count - space)), 64);
            }
            int remaining = count - space;
            outputBits = remaining == 0 ? 0 : word & ((1UL << remaining) - 1);
            outputCount = remaining;
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
