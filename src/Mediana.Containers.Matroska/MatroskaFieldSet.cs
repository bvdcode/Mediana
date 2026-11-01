// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal struct MatroskaFieldSet
    {
        private ulong seen;

        public bool TryMark(uint id, ReadOnlySpan<uint> fields)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Length, sizeof(ulong) * 8);
            int index = fields.IndexOf(id);
            if (index < 0)
            {
                return false;
            }
            ulong flag = 1UL << index;
            if ((seen & flag) != 0)
            {
                throw new InvalidDataException("A singleton Matroska metadata field is repeated.");
            }
            seen |= flag;
            return true;
        }
    }
}
