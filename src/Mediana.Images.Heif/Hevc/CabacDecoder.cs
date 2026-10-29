// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Decodes one slice's arithmetic bins according to HEVC section 9.3.4.3.
    /// </summary>
    /// <remarks>
    /// The range is normalized to 256..510 before a decision. The offset is nonnegative
    /// and smaller than the range. Both registers and all context updates belong to this
    /// decoding worker; no state is shared between slices or callers.
    /// </remarks>
    internal class CabacDecoder
    {
        private readonly BitReader reader;
        private int range = 510;
        private int offset;
        private bool terminated;

        public CabacDecoder(BitReader reader)
        {
            if (!reader.ByteAligned())
            {
                throw new InvalidDataException("CABAC must start on a byte boundary.");
            }
            this.reader = reader;
            offset = reader.Read(9);
            if (offset >= range)
            {
                throw new InvalidDataException("Invalid initial CABAC offset.");
            }
        }

        internal int Range => range;
        internal int Offset => offset;

        public bool ReadBin(ref CabacContext context)
        {
            EnsureActive();
            int lpsRange = CabacTables.LpsRange(context.State, range);
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

            Normalize();
            return value;
        }

        public bool ReadBypass()
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

        public uint ReadBypassBits(int count)
        {
            if ((count < 0) || (count > 32))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            uint value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | (ReadBypass() ? 1u : 0u);
            }
            return value;
        }

        public bool ReadTermination()
        {
            EnsureActive();
            range -= 2;
            if (offset >= range)
            {
                terminated = true;
                return true;
            }

            Normalize();
            return false;
        }

        private void Normalize()
        {
            while (range < 256)
            {
                range <<= 1;
                offset = (offset << 1) | reader.ReadBit();
            }

            if (offset >= range)
            {
                throw new InvalidDataException("CABAC offset exceeds the normalized range.");
            }
        }

        private void EnsureActive()
        {
            if (terminated)
            {
                throw new InvalidOperationException("The CABAC slice is already terminated.");
            }
        }
    }
}
