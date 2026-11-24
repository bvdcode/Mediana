// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    /// <summary>Decodes arithmetic bins under H.264 clauses 9.3.1.2 and 9.3.3.2.</summary>
    /// <remarks>On termination, returns any unread lookahead before the caller resumes reading raw bits.</remarks>
    internal class H264CabacDecoder
    {
        private const int FractionBits = 22;
        private const uint Fraction = 1u << FractionBits;
        private static readonly uint[] Decisions = CreateDecisions();
        private int range = 510;
        // The low part contains unread input followed by a single marker that tracks its length.
        private uint offset;
        private bool terminated;

        public H264CabacDecoder(ref H264BitReader bits)
        {
            if (!bits.ByteAligned)
            {
                throw new InvalidDataException("H.264 CABAC must start on a byte boundary.");
            }
            uint initialOffset = bits.ReadBits(9);
            if (initialOffset >= range)
            {
                throw new InvalidDataException("The initial H.264 CABAC offset is invalid.");
            }
            offset = (initialOffset << FractionBits) | (Fraction >> 1);
        }

        internal int Range => range;
        internal int Offset => (int)(offset >> FractionBits);
        internal int GetBitPosition(ref H264BitReader bits) => bits.BitPosition - (terminated ? 0 : BufferedBitCount);

        private int BufferedBitCount => FractionBits - 1 - BitOperations.TrailingZeroCount(offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBin(ref H264BitReader bits, ref H264CabacContext context)
        {
            EnsureActive();
            int packed = context.Packed;
            uint decision = Decisions[(packed << 2) | ((range >> 6) & 3)];
            int lpsRange = (byte)decision;
            int mpsRange = range - lpsRange;
            uint threshold = (uint)mpsRange << FractionBits;
            int lps = unchecked((int)(threshold - 1 - offset)) >> 31;
            // The normative MPS interval is at least 128, so it needs at most one shift.
            int mpsShift = 1 ^ (mpsRange >> 8);
            int lpsShift = (int)(decision >> 24);
            int shift = mpsShift + (lps & (lpsShift - mpsShift));
            context = new((byte)(decision >> (8 + (lps & 8))));
            range = (mpsRange + (lps & (lpsRange - mpsRange))) << shift;
            offset -= threshold & (uint)lps;
            Shift(ref bits, shift);
            return ((packed ^ lps) & 1) != 0;
        }

        private static uint[] CreateDecisions()
        {
            uint[] decisions = new uint[128 * 4];
            for (int packed = 0; packed < 128; packed++)
            {
                H264CabacContext context = new((byte)packed);
                uint transitions = ((uint)context.NextMostProbable().Packed << 8) | ((uint)context.NextLeastProbable().Packed << 16);
                for (int group = 0; group < 4; group++)
                {
                    int lps = H264CabacTables.LpsRange(context.State, 256 + group * 64);
                    decisions[(packed << 2) | group] = (uint)lps | transitions | ((uint)(8 - BitOperations.Log2((uint)lps)) << 24);
                }
            }
            return decisions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBypass(ref H264BitReader bits)
        {
            EnsureActive();
            return BypassCore(ref bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BypassCore(ref H264BitReader bits)
        {
            Shift(ref bits, 1);
            uint boundary = (uint)range << FractionBits;
            bool value = offset >= boundary;
            if (value)
            {
                offset -= boundary;
            }
            return value;
        }

        public uint ReadBypassBits(ref H264BitReader bits, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            EnsureActive();
            uint value = 0;
            for (int index = 0; index < count; index++)
            {
                value = (value << 1) | (BypassCore(ref bits) ? 1u : 0u);
            }
            return value;
        }

        public bool ReadTermination(ref H264BitReader bits)
        {
            EnsureActive();
            range -= 2;
            if (offset >= (uint)range << FractionBits)
            {
                bits.ReturnCabacLookahead(BufferedBitCount);
                terminated = true;
                return true;
            }
            if (range < 256)
            {
                Normalize(ref bits);
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Normalize(ref H264BitReader bits)
        {
            int shift = 8 - BitOperations.Log2((uint)range);
            range <<= shift;
            Shift(ref bits, shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Shift(ref H264BitReader bits, int shift)
        {
            offset <<= shift;
            if ((offset & (Fraction - 1)) == 0)
            {
                Refill(ref bits);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Refill(ref H264BitReader bits)
        {
            int marker = BitOperations.TrailingZeroCount(offset);
            int required = marker - (FractionBits - 1);
            uint input = bits.ReadCabacLookahead(required, FractionBits - 1 + required, out int count);
            int start = FractionBits + required - count;
            offset = (offset ^ (1u << marker)) | (input << start) | (1u << (start - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureActive()
        {
            if (terminated)
            {
                ThrowTerminated();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTerminated() =>
            throw new InvalidOperationException("The H.264 CABAC decoding process has terminated.");
    }
}
