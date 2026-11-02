// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal class MatroskaElementBudget
    {
        private readonly int maximum;
        private int count;

        public MatroskaElementBudget(int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
            this.maximum = maximum;
        }

        public void Reserve()
        {
            if (count == maximum)
            {
                throw new NotSupportedException("Matroska traversal exceeds the configured element count.");
            }
            count++;
        }
    }
}
