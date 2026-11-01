// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal class MatroskaMetadataByteBudget(long maximum)
    {
        private long used;

        public void Reserve(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > maximum - used)
            {
                throw new NotSupportedException("Matroska declarations exceed the configured metadata byte limit.");
            }
            used += count;
        }
    }
}
