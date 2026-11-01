// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaMetadataLimits(int MaximumTracks, int MaximumTextBytes, int MaximumElements)
    {
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTracks);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTextBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumElements);
        }
    }
}
