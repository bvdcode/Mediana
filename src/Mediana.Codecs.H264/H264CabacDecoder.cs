// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Decodes arithmetic bins under H.264 clauses 9.3.1.2 and 9.3.3.2.</summary>
    /// <remarks>The caller supplies the same slice reader for every operation and retains ownership of it.</remarks>
    internal class H264CabacDecoder
    {
        private int range = 510;
        private int offset;
        private bool terminated;

        public H264CabacDecoder(ref H264BitReader reader)
        {
            if (!reader.ByteAligned)
            {
                throw new InvalidDataException("H.264 CABAC must start on a byte boundary.");
            }
            offset = (int)reader.ReadBits(9);
            if (offset >= range)
            {
                throw new InvalidDataException("The initial H.264 CABAC offset is invalid.");
            }
        }

        internal int Range => range;
        internal int Offset => offset;

        public bool ReadBin(ref H264BitReader reader, ref H264CabacContext context)
        {
            EnsureActive();
            int lpsRange = H264CabacTables.LpsRange(context.State, range);
            range -= lpsRange;
            bool value = context.MostProbableSymbol;
            if (offset >= range)
            {
                offset -= range;
                range = lpsRange;
                value = !value;
                context = context.NextLeastProbable();
            }
            else
            {
                context = context.NextMostProbable();
            }
            Normalize(ref reader);
            return value;
        }

        public bool ReadBypass(ref H264BitReader reader)
        {
            EnsureActive();
            offset = (offset << 1) | reader.ReadBit();
            bool value = offset >= range;
            if (value)
            {
                offset -= range;
            }
            return value;
        }

        public uint ReadBypassBits(ref H264BitReader reader, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            EnsureActive();
            uint value = 0;
            for (int index = 0; index < count; index++)
            {
                value = (value << 1) | (ReadBypass(ref reader) ? 1u : 0u);
            }
            return value;
        }

        public bool ReadTermination(ref H264BitReader reader)
        {
            EnsureActive();
            range -= 2;
            if (offset >= range)
            {
                terminated = true;
                return true;
            }
            Normalize(ref reader);
            return false;
        }

        private void Normalize(ref H264BitReader reader)
        {
            while (range < 256)
            {
                range <<= 1;
                offset = (offset << 1) | reader.ReadBit();
            }
            if (offset >= range)
            {
                throw new InvalidDataException("The H.264 CABAC offset exceeds the normalized range.");
            }
        }

        private void EnsureActive()
        {
            if (terminated)
            {
                throw new InvalidOperationException("The H.264 CABAC decoding process has terminated.");
            }
        }
    }
}
